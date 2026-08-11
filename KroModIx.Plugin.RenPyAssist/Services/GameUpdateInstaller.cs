using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public GameUpdateInstaller(GamesRegistry registry)
    {
        _registry = registry;
    }

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

            // 2. ZIP entpacken.
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var topLevelDirs = archive.Entries
                    .Select(e => e.FullName.Split('/', '\\')[0])
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Flache ZIP (game/ direkt) → wir wrappen in einen Sub-Ordner
                // aus dem ZIP-Namen (ohne .zip-Extension).
                if (topLevelDirs.Contains("game", StringComparer.OrdinalIgnoreCase))
                {
                    var wrapName = Path.GetFileNameWithoutExtension(zipPath);
                    var wrapDir = Path.Combine(game.ContainerPath, wrapName);
                    Directory.CreateDirectory(wrapDir);
                    archive.ExtractToDirectory(wrapDir, overwriteFiles: true);
                }
                else
                {
                    archive.ExtractToDirectory(game.ContainerPath, overwriteFiles: true);
                }
            }, ct);

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
            if (hadSaves)
            {
                var newSavesDir = Path.Combine(newSubDir, "game", "saves");
                try
                {
                    CopyDirectoryRecursive(oldSavesDir, newSavesDir);
                    Log.Info("Saves kopiert: {From} → {To}", oldSavesDir, newSavesDir);
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Save-Copy fehlgeschlagen — neue Installation ohne Saves");
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
                && !string.Equals(oldSubPath, newSubName, StringComparison.Ordinal))
            {
                var oldFullDir = Path.Combine(game.ContainerPath, oldSubPath);
                if (Directory.Exists(oldFullDir))
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

            // 7. Registry aktualisieren — LocalVersion aus neuem Sub-Ordner-Namen.
            //    LastRemoteVersion auf LocalVersion setzen: der User hat gerade
            //    das aktuelle Remote-Update installiert, ergo HasUpdate=false.
            //    Version-Format-Mismatches (v0.1.3 vs 0.1.3) waeren sonst ein
            //    Problem. Der nächste Worker-Check holt frisch — falls wirklich
            //    ein noch neueres Update rauskommt, zeigt der Badge dann wieder.
            game.ActiveSubPath = newSubName;
            game.LocalVersion = RenPyGameDetector.ExtractVersion(newSubName);
            if (!string.IsNullOrEmpty(game.LocalVersion))
                game.LastRemoteVersion = game.LocalVersion;
            _registry.Update(game);

            return InstallResult.Ok(newSubName, oldSubPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update-Install fehlgeschlagen für {Container}", game.ContainerPath);
            return InstallResult.Fail(ex.Message);
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
    public static InstallResult Ok(string newSubPath, string? oldSubPath)
        => new(true, newSubPath, oldSubPath, null);
    public static InstallResult Fail(string error)
        => new(false, null, null, error);
}
