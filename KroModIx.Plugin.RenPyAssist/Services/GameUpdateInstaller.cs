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

            // 1. Ziel VOR dem Entpacken bestimmen.
            //
            // v0.29.0: vorher wurde ausgepackt und danach per Verzeichnis-Diff
            // gesucht, welcher Unterordner neu dazugekommen ist. Das scheitert,
            // sobald das Archiv denselben Ordnernamen traegt wie die
            // installierte Fassung — Entwickler benennen den Build-Ordner
            // zwischen Versionen nicht immer um. Real: ein Archiv „…v0.3…zip"
            // mit dem Ordner „THEPLEASURESOFEGYPT-0.2-win" darin, exakt der
            // Name der installierten 0.2. Der Diff fand nichts Neues und
            // meldete Fehlschlag — nachdem alle Dateien bereits ueber die
            // laufende Fassung geschrieben waren.
            if (_archives.DetectKind(zipPath) == ArchiveKind.Unknown)
                return InstallResult.Fail(
                    "Das ist kein lesbares Archiv (ZIP/RAR/7z) — eventuell ein "
                    + "abgebrochener Download.");

            var ziel = UpdateTargetResolver.Resolve(_archives, zipPath, game.ContainerPath);
            if (ziel is null)
                return InstallResult.Fail(
                    "Im Archiv ist kein game/-Ordner zu finden — das ist kein Ren'Py-Build. "
                    + "Der alte Stand ist unangetastet.");

            var newSubName = ziel.SubFolderName;
            var newSubDir = Path.Combine(game.ContainerPath, newSubName);

            // 2. Spielstaende sichern, BEVOR geschrieben wird.
            //
            // Beim In-Place-Fall schreibt das Archiv in genau den Ordner, in
            // dem die Spielstaende liegen. Sie erst danach zu kopieren — so
            // lief es bis v0.28 — kommt zu spaet, wenn das Archiv selbst ein
            // game/saves mitbringt.
            string? savesBackup = null;
            if (hadSaves && ziel.IsInPlace)
            {
                savesBackup = Path.Combine(Path.GetTempPath(),
                    "renpyassist-saves-" + Guid.NewGuid().ToString("N"));
                try
                {
                    CopyDirectoryRecursive(oldSavesDir, savesBackup);
                    if (!VerifySavesCopied(oldSavesDir, savesBackup))
                        return InstallResult.Fail(
                            "Die Spielstaende konnten vor dem Einbau nicht vollstaendig "
                            + "gesichert werden — abgebrochen, der alte Stand ist unangetastet.");
                    Log.Info("Saves vorab gesichert: {From} → {To}", oldSavesDir, savesBackup);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Vorab-Sicherung der Saves fehlgeschlagen: {From}", oldSavesDir);
                    return InstallResult.Fail(
                        "Die Spielstaende konnten vor dem Einbau nicht gesichert werden ("
                        + ex.Message + ") — abgebrochen, der alte Stand ist unangetastet.");
                }
            }

            // 3. Archiv entpacken.
            //
            // v0.21.0: ZIP, RAR und 7z statt nur ZIP. f95zone-Releases kommen
            // in allen dreien.
            // v0.23.0: ueber IHostServices.Archives statt SharpCompress im Plugin.
            var abgelehnt = await Task.Run(() =>
            {
                Directory.CreateDirectory(ziel.ExtractInto);
                return _archives.Extract(zipPath, ziel.ExtractInto).SkippedUnsafe;
            }, ct).ConfigureAwait(false);

            // Ein Ausbruchsversuch beendet den Einbau hier, statt die Dateien
            // zu nehmen die durchkamen.
            if (abgelehnt.Count > 0)
            {
                Log.Warn("Ausbruchsversuch im Update-Archiv, {Count} Eintrag/Eintraege "
                    + "abgelehnt: {Entries}", abgelehnt.Count, string.Join(", ", abgelehnt));
                TryRestoreSaves(savesBackup, oldSavesDir);
                return InstallResult.Fail(
                    $"Abgebrochen: {abgelehnt.Count} Eintrag/Eintraege im Archiv wollten aus "
                    + "dem Spielordner herausschreiben — "
                    + string.Join(", ", abgelehnt.Take(3))
                    + (abgelehnt.Count > 3 ? ", …" : "")
                    + ". Der alte Stand samt Spielstaenden ist unangetastet.");
            }

            if (!Directory.Exists(Path.Combine(newSubDir, "game")))
            {
                TryRestoreSaves(savesBackup, oldSavesDir);
                return InstallResult.Fail(
                    $"Nach dem Entpacken fehlt {newSubName}/game/ — das Archiv enthaelt "
                    + "offenbar keinen vollstaendigen Ren'Py-Build.");
            }

            // 4. Spielstaende an ihren Platz bringen.
            //
            // Zwei Faelle, seit v0.29.0 unterschieden:
            //   In-Place  — der Ordner ist derselbe. Die Saves liegen schon
            //               richtig; nur falls das Archiv sie ueberschrieben
            //               oder geloescht hat, kommt die Vorab-Sicherung
            //               zurueck.
            //   Neuer Ord.— vom alten in den neuen kopieren, wie bisher.
            bool savesSecured = !hadSaves;
            string? savesWarning = null;
            var newSavesDir = Path.Combine(newSubDir, "game", "saves");

            if (hadSaves && ziel.IsInPlace)
            {
                // Hat das Archiv die Saves mitgebracht und damit ueberbuegelt,
                // stellt die Vorab-Sicherung den alten Stand wieder her. Gibt
                // es sie unveraendert, passiert nichts.
                savesSecured = VerifySavesCopied(savesBackup!, newSavesDir);
                if (!savesSecured)
                {
                    Log.Warn("Saves nach dem Einbau unvollstaendig — Vorab-Sicherung wird "
                        + "zurueckgespielt: {From} → {To}", savesBackup, newSavesDir);
                    savesSecured = TryRestoreSaves(savesBackup, newSavesDir);
                    if (!savesSecured)
                        savesWarning = string.Format(Strings.T("install.saves_backup_kept"), savesBackup);
                }
                else
                {
                    Log.Info("Saves unveraendert an Ort und Stelle: {Dir}", newSavesDir);
                }
            }
            else if (hadSaves)
            {
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
            // v0.29.0: Faellt der Ordnername als Versionsquelle aus, zaehlt der
            // Archivname. Beim In-Place-Fall traegt der Ordner die ALTE Version
            // (Entwickler hat ihn nicht umbenannt) — uebernaehme man sie, bliebe
            // der Update-Badge fuer immer stehen.
            var ausOrdner = RenPyGameDetector.ExtractVersion(newSubName);
            var ausArchiv = RenPyGameDetector.ExtractVersion(Path.GetFileName(zipPath));
            game.LocalVersion = WähleVersion(ausOrdner, ausArchiv, game.LocalVersion);

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

            // Die Vorab-Sicherung nur wegraeumen, wenn die Saves sicher am
            // Platz sind. Sonst bleibt sie liegen und steht in der Warnung.
            if (savesBackup is not null && savesSecured) TryDeleteDir(savesBackup);

            return InstallResult.Ok(newSubName,
                ziel.IsInPlace ? null : oldSubPath, savesWarning);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update-Install fehlgeschlagen für {Container}", game.ContainerPath);
            return InstallResult.Fail(ex.Message);
        }
    }


    /// <summary>Welche der beiden Versionsangaben gilt. Der Ordnername hat
    /// Vorrang, solange er ueberhaupt eine traegt und sie nicht aelter ist als
    /// die bisherige — sonst der Archivname.
    ///
    /// <para>Hintergrund: beim In-Place-Update traegt der Ordner die alte
    /// Version, weil der Entwickler ihn nicht umbenannt hat. Uebernaehme man
    /// sie, bliebe das Update-Badge nach einem erfolgreichen Einbau
    /// stehen.</para></summary>
    internal static string? WähleVersion(string? ausOrdner, string? ausArchiv, string? bisher)
    {
        if (string.IsNullOrWhiteSpace(ausOrdner)) return ausArchiv ?? bisher;
        if (string.IsNullOrWhiteSpace(ausArchiv)) return ausOrdner;
        // Beide da: die neuere gewinnt. VersionCompare kennt die Regeln
        // (0.10 > 0.9, 1.0 == 1.0.0, Hotfix-Buchstaben).
        return VersionCompare.IsRemoteNewer(ausArchiv, ausOrdner) ? ausArchiv : ausOrdner;
    }

    /// <summary>Spielt eine Vorab-Sicherung zurueck. true = Ziel ist danach
    /// vollstaendig.</summary>
    private static bool TryRestoreSaves(string? backupDir, string targetDir)
    {
        if (backupDir is null || !Directory.Exists(backupDir)) return false;
        try
        {
            CopyDirectoryRecursive(backupDir, targetDir);
            var ok = VerifySavesCopied(backupDir, targetDir);
            if (ok) Log.Info("Saves zurueckgespielt: {From} → {To}", backupDir, targetDir);
            else Log.Error("Rueckspielen unvollstaendig — Sicherung bleibt: {Dir}", backupDir);
            return ok;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Rueckspielen der Saves fehlgeschlagen — Sicherung bleibt: {Dir}", backupDir);
            return false;
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Log.Debug(ex, "Temp-Sicherung nicht aufraeumbar: {Dir}", dir); }
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
