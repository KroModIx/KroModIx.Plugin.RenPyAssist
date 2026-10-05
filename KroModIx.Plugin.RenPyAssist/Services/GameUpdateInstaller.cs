using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Installiert eine neue Version aus einer ZIP-Datei in den
/// Container-Ordner eines Ren'Py-Spiels. Sub-Path-Rotation-Pattern:
///
/// <list type="number">
/// <item>ZIP nach <c>ContainerPath/</c> entpacken → neuer Version-Sub-Ordner.</item>
/// <item><c>game/saves/</c> aus dem alten Sub-Ordner in den neuen kopieren
///   (Save-Games bleiben erhalten).</item>
/// <item>Registry-Eintrag umschreiben: <see cref="RenPyGame.ActiveSubPath"/>
///   auf den neuen Sub-Ordner, <see cref="RenPyGame.LocalVersion"/> aus dem
///   Namen extrahiert.</item>
/// <item>Alter Sub-Ordner bleibt liegen — User räumt manuell auf (Safety-
///   Net falls neue Version broken ist).</item>
/// </list>
///
/// <para><b>ZIP-Struktur-Erkennung:</b> Ren'Py-ZIPs kommen typischerweise
/// mit dem Version-Sub-Ordner als Top-Level-Eintrag (z. B.
/// <c>MyGame-0.240-pc/game/</c>). Falls die ZIP flach ist (<c>game/</c>
/// direkt), erzeugen wir einen Sub-Ordner mit dem Namen der ZIP-Datei.</para></summary>
public sealed class GameUpdateInstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly GamesRegistry _registry;
    private readonly IArchiveService _archives;

    public GameUpdateInstaller(GamesRegistry registry, IArchiveService archives)
    {
        _registry = registry;
        _archives = archives;
    }

    /// <summary>Endungs-Vorfilter fuer die Archiv-Auswahl. Kommt aus dem
    /// Host-Baukasten, damit ein dort neu unterstuetztes Format nicht in
    /// neun Plugins nachgetragen werden muss.</summary>
    public IReadOnlyList<string> SupportedExtensions => _archives.SupportedExtensions;

    public bool HasSupportedExtension(string path) => _archives.HasSupportedExtension(path);

    public async Task<InstallResult> InstallAsync(RenPyGame game, string zipPath, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            return InstallResult.Fail("ZIP-Datei nicht gefunden");
        if (!Directory.Exists(game.ContainerPath))
            return InstallResult.Fail("Container-Ordner existiert nicht mehr");

        try
        {
            // Alten Sub-Path (für Save-Copy) merken bevor wir extrahieren.
            var oldSubPath = game.ActiveSubPath;
            var oldSavesDir = oldSubPath is null
                ? Path.Combine(game.ContainerPath, "game", "saves")
                : Path.Combine(game.ContainerPath, oldSubPath, "game", "saves");
            var hadSaves = Directory.Exists(oldSavesDir);

            // 1. Vor-Extract: welche Sub-Ordner gab's schon? (Diff nach
            //    Extract = neuer Sub-Ordner.)
            var subsBefore = Directory.EnumerateDirectories(game.ContainerPath)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 2. Archiv entpacken.
            //
            // v0.21.0: ZIP, RAR und 7z statt nur ZIP. f95zone-Releases kommen
            // in allen dreien; vorher scheiterte ein RAR-Download erst beim
            // Entpacken mit einer Format-Exception.
            // v0.23.0: ueber IHostServices.Archives statt SharpCompress im
            // Plugin — und mit einer Meldung statt eines stillen
            // Uebersprungs, siehe unten.
            if (_archives.DetectKind(zipPath) == ArchiveKind.Unknown)
                return InstallResult.Fail(
                    "Das ist kein lesbares Archiv (ZIP/RAR/7z) — eventuell ein "
                    + "abgebrochener Download.");

            var abgelehnt = await Task.Run(() =>
            {
                var entries = _archives.List(zipPath);
                var topLevelDirs = entries
                    .Select(e => e.Path.Split('/')[0])
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Flaches Archiv (game/ direkt) → wir wrappen in einen
                // Sub-Ordner aus dem Archiv-Namen (ohne Extension).
                var target = topLevelDirs.Contains("game", StringComparer.OrdinalIgnoreCase)
                    ? Path.Combine(game.ContainerPath, Path.GetFileNameWithoutExtension(zipPath))
                    : game.ContainerPath;
                Directory.CreateDirectory(target);

                var r = _archives.Extract(zipPath, target);
                return r.SkippedUnsafe;
            }, ct).ConfigureAwait(false);

            // Ein Ausbruchsversuch beendet den Einbau hier, statt die Dateien
            // zu nehmen die durchkamen. Vorher wurde der Eintrag nur
            // protokolliert und der Einbau lief weiter — mit dem Ergebnis,
            // dass die Schritte 3 bis 6 auf einem halb entpackten Stand
            // arbeiteten und am Ende der alte Unterordner mit den
            // Spielstaenden geloescht worden waere.
            if (abgelehnt.Count > 0)
            {
                Log.Warn("Ausbruchsversuch im Update-Archiv, {Count} Eintrag/Eintraege "
                    + "abgelehnt: {Entries}", abgelehnt.Count, string.Join(", ", abgelehnt));
                return InstallResult.Fail(
                    $"Abgebrochen: {abgelehnt.Count} Eintrag/Eintraege im Archiv wollten aus "
                    + "dem Spielordner herausschreiben — "
                    + string.Join(", ", abgelehnt.Take(3))
                    + (abgelehnt.Count > 3 ? ", …" : "")
                    + ". Der alte Stand samt Spielstaenden ist unangetastet.");
            }

            // 3. Neuen Sub-Ordner ermitteln (diff gegen subsBefore + game/-Marker).
            var subsAfter = Directory.EnumerateDirectories(game.ContainerPath).ToList();
            var newSubDir = subsAfter
                .Where(d => !subsBefore.Contains(Path.GetFileName(d))
                         && Directory.Exists(Path.Combine(d, "game")))
                .OrderByDescending(d => Directory.GetCreationTimeUtc(d))
                .FirstOrDefault();

            if (newSubDir is null)
                return InstallResult.Fail("Nach dem Entpacken kein neuer Sub-Ordner mit game/ gefunden");

            var newSubName = Path.GetFileName(newSubDir);

            // 4. Save-Games kopieren.
            //
            // v0.19.0: Das Ergebnis entscheidet, ob der alte Sub-Ordner in
            // Schritt 6 geloescht werden darf. Vorher wurde ein
            // fehlgeschlagener Copy nur geloggt und trotzdem weiter geloescht
            // — bei voller Platte, gesperrter Datei oder Rechte-Problem waren
            // die Spielstaende danach unwiederbringlich weg.
            bool savesSecured = !hadSaves;
            string? savesWarning = null;
            if (hadSaves)
            {
                var newSavesDir = Path.Combine(newSubDir, "game", "saves");
                try
                {
                    CopyDirectoryRecursive(oldSavesDir, newSavesDir);
                    savesSecured = VerifySavesCopied(oldSavesDir, newSavesDir);
                    if (savesSecured)
                        Log.Info("Saves kopiert: {From} → {To}", oldSavesDir, newSavesDir);
                    else
                        Log.Error("Save-Copy unvollstaendig: {From} → {To} — alter Ordner bleibt stehen",
                            oldSavesDir, newSavesDir);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Save-Copy fehlgeschlagen — alter Ordner bleibt stehen: {From}", oldSavesDir);
                }
            }

            // 5. ZIP archivieren im Container/archive/ — für spätere Reinstalls,
            //    Rollbacks und Backup-Zwecke wandert die Original-ZIP ins Spiel-
            //    Verzeichnis mit.
            try
            {
                var archiveDir = Path.Combine(game.ContainerPath, "archive");
                Directory.CreateDirectory(archiveDir);
                var archiveTarget = Path.Combine(archiveDir, Path.GetFileName(zipPath));
                if (!string.Equals(Path.GetFullPath(zipPath), Path.GetFullPath(archiveTarget),
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(zipPath, archiveTarget, overwrite: true);
                    Log.Info("ZIP archiviert: {From} → {To}", zipPath, archiveTarget);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "ZIP-Archivierung fehlgeschlagen (nicht kritisch)");
            }

            // 6. Alten Sub-Ordner löschen (User-Wunsch v0.8.1: kein Safety-Net
            //    mehr). Nur wenn wirklich ein alter Sub-Path existierte UND
            //    er nicht identisch zum neuen ist (defensive check).
            if (!string.IsNullOrEmpty(oldSubPath)
                && !string.Equals(oldSubPath, newSubName, StringComparison.Ordinal)
                && savesSecured)
            {
                var oldFullDir = Path.Combine(game.ContainerPath, oldSubPath);
                // v0.24.0: NICHT loeschen, wenn der alte Unterordner ein
                // Verweis ist oder einem anderen Werkzeug gehoert. Das ist die
                // destruktivste Stelle im ganzen Plugin — rekursiv, im
                // Spielordner des Nutzers, und dort liegen Spielstaende. Wer
                // seine Fassungen auf eine andere Platte verweist, soll hier
                // nicht ueberrascht werden; stattdessen bleibt der Ordner
                // stehen und es steht im Protokoll.
                if (ForeignManagerDetection.IsForeignManaged(oldFullDir, out var verwalter))
                {
                    Log.Info("Alter Sub-Ordner bleibt stehen, er ist ein Verweis bzw. "
                        + "gehoert {Verwalter}: {Old}", verwalter, oldFullDir);
                }
                else if (Directory.Exists(oldFullDir))
                {
                    try
                    {
                        Directory.Delete(oldFullDir, recursive: true);
                        Log.Info("Alter Sub-Ordner gelöscht: {Old}", oldFullDir);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(ex, "Auto-Delete des alten Sub-Ordners fehlgeschlagen: {Old}", oldFullDir);
                    }
                }
            }

            else if (!savesSecured)
            {
                // Kein stiller Datenverlust: der User muss wissen, dass der
                // alte Ordner absichtlich stehen bleibt und seine Saves dort
                // noch liegen.
                Log.Warn("Alter Sub-Ordner {Old} wurde NICHT geloescht — Saves konnten nicht "
                         + "uebernommen werden", oldSubPath);
                savesWarning = string.Format(Strings.T("install.saves_not_copied"), oldSubPath);
            }

            // 7. Registry aktualisieren — LocalVersion aus neuem Sub-Ordner-Namen.
            //    LastRemoteVersion auf LocalVersion setzen: der User hat gerade
            //    das aktuelle Remote-Update installiert, ergo HasUpdate=false.
            //    Version-Format-Mismatches (v0.1.3 vs 0.1.3) waeren sonst ein
            //    Problem. Der nächste Worker-Check holt frisch — falls wirklich
            //    ein noch neueres Update rauskommt, zeigt der Badge dann wieder.
            game.ActiveSubPath = newSubName;
            game.LocalVersion = RenPyGameDetector.ExtractVersion(newSubName);

            // v0.23.0: frisch entpackt heisst unter Linux fast immer „ohne
            // Exec-Bits" — ZIP transportiert keine Unix-Permissions. Hier
            // nachziehen, damit das Spiel sofort startet, auch von Hand.
            var patched = ExecutableBits.Apply(Path.Combine(game.ContainerPath, newSubName));
            if (patched > 0)
                Log.Info("ExecutableBits: +x auf {N} Datei(en) nach dem Update in {Sub}",
                    patched, newSubName);
            if (!string.IsNullOrEmpty(game.LocalVersion))
                game.LastRemoteVersion = game.LocalVersion;
            _registry.Update(game);

            return InstallResult.Ok(newSubName, oldSubPath, savesWarning);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update-Install fehlgeschlagen für {Container}", game.ContainerPath);
            return InstallResult.Fail(ex.Message);
        }
    }


    /// <summary>Zaehlt Dateien und Gesamtgroesse in Quelle und Ziel gegen.
    /// Ein Copy, der die Haelfte geschafft hat und dann abgebrochen ist,
    /// zaehlt als NICHT gesichert — sonst faellt der Loesch-Schritt darauf
    /// rein.</summary>
    private static bool VerifySavesCopied(string source, string destination)
    {
        try
        {
            if (!Directory.Exists(destination)) return false;
            var srcFiles = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            var dstFiles = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
            if (dstFiles.Length < srcFiles.Length) return false;
            long srcBytes = srcFiles.Sum(f => new FileInfo(f).Length);
            long dstBytes = dstFiles.Sum(f => new FileInfo(f).Length);
            return dstBytes >= srcBytes;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Save-Verifikation fehlgeschlagen — als unsicher behandelt");
            return false;
        }
    }

    private static void CopyDirectoryRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
        {
            var target = Path.Combine(dst, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }
        foreach (var sub in Directory.EnumerateDirectories(src))
        {
            CopyDirectoryRecursive(sub, Path.Combine(dst, Path.GetFileName(sub)));
        }
    }
}

public sealed record InstallResult(bool Success, string? NewSubPath, string? OldSubPath, string? Error)
{
    /// <summary>v0.19.0: gesetzt, wenn der Install zwar durchlief, die Saves
    /// aber nicht uebernommen werden konnten und der alte Sub-Ordner deshalb
    /// absichtlich stehen geblieben ist. Der Aufrufer MUSS das anzeigen —
    /// sonst wundert sich der User ueber den doppelten Ordner und startet
    /// womoeglich die neue Version ohne seine Spielstaende.</summary>
    public string? Warning { get; init; }

    public static InstallResult Ok(string newSubPath, string? oldSubPath, string? warning = null)
        => new(true, newSubPath, oldSubPath, null) { Warning = warning };
    public static InstallResult Fail(string error)
        => new(false, null, null, error);
}
