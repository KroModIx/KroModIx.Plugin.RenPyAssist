using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Was beim Anlegen eines neuen Spiels herauskam.</summary>
/// <param name="ContainerPath">Der angelegte Container, oder null bei Fehler.</param>
/// <param name="SubPath">Der Build-Unterordner innerhalb des Containers.</param>
public sealed record NewGameResult(
    bool Success, string? ContainerPath, string? SubPath, string? Error)
{
    public static NewGameResult Fail(string error) => new(false, null, null, error);
    public static NewGameResult Ok(string container, string sub) => new(true, container, sub, null);
}

/// <summary>Legt aus einem Archiv im Downloads-Ordner ein neues Ren'Py-Spiel
/// an: Container bauen, auspacken, Archiv dazulegen, Exec-Bits setzen.
///
/// <para><b>Warum als eigener Dienst und nicht im Update-Installer:</b> der
/// Update-Weg hat einen bestehenden Container mit Spielständen, die er
/// schützen muss, und einen alten Unterordner, den er am Ende aufräumt. Hier
/// gibt es nichts zu schützen und nichts aufzuräumen — dafür die Frage, wie
/// der Container heißen soll, die der Update-Weg nie stellt. Die beiden
/// zusammenzulegen hätte den destruktiven Pfad um Verzweigungen erweitert,
/// die er nicht braucht.</para>
///
/// <para>Der Dienst legt bewusst dieselbe Struktur an, die hier von Hand
/// entstanden ist, damit beides nebeneinander funktioniert:</para>
/// <code>
/// &lt;Root&gt;/&lt;Spielname&gt;/
///     &lt;Build-Ordner&gt;/game/…      aus dem Archiv
///     archive/&lt;Archivdatei&gt;      das Archiv wandert mit
/// </code></summary>
public sealed class GameInstallService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IArchiveService _archives;

    public GameInstallService(IArchiveService archives) => _archives = archives;

    /// <summary>Der Ordner, unter dem die Spiele liegen — abgeleitet aus dem
    /// häufigsten Elternverzeichnis der bereits registrierten Container.
    ///
    /// <para>Es gibt keine Einstellung dafür, die verlässlich gefüllt wäre
    /// (<c>gamesRoot</c> steht in der Praxis leer, weil die Spiele über den
    /// Host-Wizard kamen, nicht über das Plugin). Das Mehrheitsverfahren ist
    /// robuster als ein Default-Pfad: es folgt dem, was der User tatsächlich
    /// tut, auch wenn einzelne Spiele woanders liegen.</para>
    ///
    /// <para>Null, wenn es noch kein Spiel gibt — dann muss der Aufrufer
    /// fragen.</para></summary>
    public static string? GuessGamesRoot(IEnumerable<RenPyGame> games)
    {
        var parents = games
            .Select(g => g.ContainerPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetDirectoryName(p!.TrimEnd('/', '\\')))
            .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            .GroupBy(p => p!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)   // stabil bei Gleichstand
            .FirstOrDefault();
        return parents?.Key;
    }

    /// <summary>Der Build-Ordner, den das Archiv mitbringt — der oberste
    /// Ordner, in dem <c>game/</c> liegt. Null, wenn das Archiv flach ist
    /// (<c>game/</c> direkt auf oberster Ebene); dann baut der Aufrufer den
    /// Ordnernamen selbst.</summary>
    public string? DetectBuildFolder(string archivePath)
    {
        try
        {
            var entries = _archives.List(archivePath);
            // "Foo-1.0-pc/game/script.rpa" -> Build-Ordner "Foo-1.0-pc"
            var mitGame = entries
                .Select(e => e.Path.Split('/'))
                .Where(p => p.Length >= 2
                            && string.Equals(p[1], "game", StringComparison.OrdinalIgnoreCase))
                .Select(p => p[0])
                .FirstOrDefault(p => !string.IsNullOrEmpty(p));
            return mitGame;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Archiv-Inhalt nicht lesbar: {Path}", archivePath);
            return null;
        }
    }

    /// <summary>Prüft vorab, ob das Archiv überhaupt ein Ren'Py-Spiel ist —
    /// also irgendwo einen <c>game/</c>-Ordner trägt. Erspart dem User, erst
    /// nach dem Auspacken von 6 GB zu erfahren, dass er die falsche Datei
    /// erwischt hat.</summary>
    public bool LooksLikeRenPyGame(string archivePath)
    {
        try
        {
            return _archives.List(archivePath).Any(e =>
                e.Path.Split('/').Any(seg =>
                    string.Equals(seg, "game", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Archiv-Inhalt nicht lesbar: {Path}", archivePath);
            return false;
        }
    }

    /// <summary>Legt das Spiel an. <paramref name="displayName"/> wird zum
    /// Container-Ordnernamen; <paramref name="moveArchive"/> verschiebt die
    /// Archivdatei nach <c>archive/</c> im Container statt sie zu kopieren.</summary>
    public async Task<NewGameResult> InstallAsync(
        string archivePath, string gamesRoot, string displayName,
        bool moveArchive = true, CancellationToken ct = default)
    {
        if (!File.Exists(archivePath)) return NewGameResult.Fail($"Archiv fehlt: {archivePath}");
        if (!Directory.Exists(gamesRoot)) return NewGameResult.Fail($"Spiele-Ordner fehlt: {gamesRoot}");

        var safeName = SanitizeFolderName(displayName);
        if (safeName.Length == 0) return NewGameResult.Fail("Der Name ergibt keinen gültigen Ordnernamen.");

        var container = Path.Combine(gamesRoot, safeName);
        if (Directory.Exists(container))
            return NewGameResult.Fail($"Es gibt schon einen Ordner '{safeName}'. "
                + "Nimm einen anderen Namen oder installiere das Update über die bestehende Kachel.");

        // Format am Inhalt pruefen, nicht an der Endung — ein .zip, das in
        // Wahrheit etwas anderes ist, landete sonst halb entpackt im Ordner.
        if (_archives.DetectKind(archivePath) == ArchiveKind.Unknown)
            return NewGameResult.Fail("Die Datei ist kein lesbares ZIP, RAR oder 7z.");

        string? erstellt = null;
        try
        {
            var buildFolder = DetectBuildFolder(archivePath);
            Directory.CreateDirectory(container);
            erstellt = container;

            // Flaches Archiv (game/ ganz oben) bekommt einen Build-Ordner aus
            // dem Archivnamen — so sieht die Struktur hinterher gleich aus,
            // egal wie das Archiv gepackt war, und die Sub-Path-Rotation beim
            // naechsten Update funktioniert.
            var subName = buildFolder ?? Path.GetFileNameWithoutExtension(archivePath);
            var target = buildFolder is null ? Path.Combine(container, subName) : container;
            Directory.CreateDirectory(target);

            var result = await Task.Run(() => _archives.Extract(archivePath, target), ct)
                .ConfigureAwait(false);

            if (result.SkippedUnsafe.Count > 0)
            {
                // Wie im Update-Pfad: ein Ausbruchsversuch beendet den Einbau,
                // statt mit dem zu arbeiten, was durchkam.
                TryDelete(container);
                return NewGameResult.Fail(
                    $"Abgebrochen: {result.SkippedUnsafe.Count} Eintrag/Einträge im Archiv wollten "
                    + "aus dem Spielordner herausschreiben — "
                    + string.Join(", ", result.SkippedUnsafe.Take(3))
                    + (result.SkippedUnsafe.Count > 3 ? ", …" : ""));
            }

            var buildDir = Path.Combine(container, subName);
            if (!Directory.Exists(Path.Combine(buildDir, "game")))
            {
                TryDelete(container);
                return NewGameResult.Fail(
                    "Nach dem Auspacken ist kein game/-Ordner da — das Archiv enthält "
                    + "offenbar kein Ren'Py-Spiel.");
            }

            // ZIP transportiert keine Unix-Permissions; ohne das hier startet
            // das Spiel nicht und sagt nicht warum (v0.25.0).
            var bits = ExecutableBits.Apply(buildDir);
            if (bits > 0) Log.Info("Neues Spiel: +x auf {N} Datei(en) in {Dir}", bits, buildDir);

            StoreArchive(archivePath, container, moveArchive);

            Log.Info("Neues Ren'Py-Spiel angelegt: {Name} ({Container}, sub={Sub}, {Files} Dateien)",
                displayName, container, subName, result.Count);
            return NewGameResult.Ok(container, subName);
        }
        catch (OperationCanceledException)
        {
            if (erstellt is not null) TryDelete(erstellt);
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Anlegen fehlgeschlagen: {Archive}", archivePath);
            if (erstellt is not null) TryDelete(erstellt);
            return NewGameResult.Fail(ex.Message);
        }
    }

    /// <summary>Legt das Archiv in <c>archive/</c> im Container ab. Scheitert
    /// das Verschieben (anderes Dateisystem, Datei in Benutzung), wird kopiert;
    /// scheitert auch das, bleibt das Archiv wo es war — das ist kein Grund,
    /// die fertige Installation zu verwerfen.</summary>
    private static void StoreArchive(string archivePath, string container, bool move)
    {
        try
        {
            var archiveDir = Path.Combine(container, "archive");
            Directory.CreateDirectory(archiveDir);
            var ziel = Path.Combine(archiveDir, Path.GetFileName(archivePath));
            if (File.Exists(ziel)) return;
            if (move) File.Move(archivePath, ziel);
            else File.Copy(archivePath, ziel);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Archiv konnte nicht in den Container gelegt werden: {Archive}", archivePath);
        }
    }

    /// <summary>Zeichen, die im Ordnernamen zu einem Leerzeichen werden.
    ///
    /// <para>Feste Liste statt <see cref="Path.GetInvalidFileNameChars"/>:
    /// die ist plattformabhängig und liefert unter Linux nur <c>/</c> und
    /// <c>\0</c>. Ein hier angelegter Ordner „Lisa: TI" wäre dann unter
    /// Linux in Ordnung und auf einer Windows-Maschine, die dieselbe Platte
    /// liest, nicht mehr zu öffnen. Die Spiele liegen auf einer externen
    /// Platte — dieser Fall ist nicht theoretisch.</para></summary>
    private static readonly char[] VerboteneZeichen =
        { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };

    /// <summary>Entfernt aus einem Anzeigenamen, was in einem Ordnernamen
    /// nicht vorkommen darf, und schneidet führende/abschließende Punkte und
    /// Leerzeichen ab (Windows mag beides am Ende nicht).</summary>
    public static string SanitizeFolderName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;
        var invalid = VerboteneZeichen
            .Concat(Path.GetInvalidFileNameChars())
            .Distinct()
            .ToArray();
        var cleaned = new string(displayName.Select(c => invalid.Contains(c) ? ' ' : c).ToArray());
        while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
        return cleaned.Trim().Trim('.').Trim();
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Log.Warn(ex, "Aufräumen nach Fehlschlag misslang: {Dir}", dir); }
    }
}
