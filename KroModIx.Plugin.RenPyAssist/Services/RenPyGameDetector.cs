using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Erkennt Ren'Py-Spiele in einem Root-Ordner. Ren'Py-Spiele
/// haben immer einen <c>game/</c>-Sub-Ordner (universal, seit Ren'Py 6.0).
///
/// <para><b>Sub-Path-Rotation-Erkennung</b> (aus RenPack
/// <c>GamesRegistry.DetectRenpySubFolder</c>):</para>
///
/// <list type="number">
/// <item><b>Prio 1:</b> Container hat direkten <c>game/</c>-Sub-Ordner
///   → das ist die aktive Version, <c>ActiveSubPath = null</c> (Legacy).</item>
/// <item><b>Prio 2:</b> Container hat Version-Sub-Ordner (z. B.
///   <c>MyGame-0.230-pc/</c>) mit <c>game/</c> drin → dieser ist die
///   aktive Version, <c>ActiveSubPath = &lt;subdir-name&gt;</c>.</item>
/// <item><b>Prio 3:</b> Container hat mehrere Sub-Ordner → nimm den mit
///   der höchsten extrahierbaren Version.</item>
/// </list>
/// </summary>
public static class RenPyGameDetector
{
    // v0.230, v1.2.3-beta, v0.4.4a — greift die numerische Kernversion
    private static readonly Regex VersionInFolderRegex = new(
        @"[-_\s.v]?(\d+(?:\.\d+)+[a-z0-9]*)(?:[-_\s]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Scannt <paramref name="root"/> nach Ren'Py-Spielen.
    /// Ergebnis: pro Container ein <see cref="RenPyGame"/> mit gesetztem
    /// ActiveSubPath und LocalVersion.</summary>
    public static IReadOnlyList<RenPyGame> Scan(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return System.Array.Empty<RenPyGame>();

        var found = new List<RenPyGame>();
        foreach (var containerDir in Directory.EnumerateDirectories(root))
        {
            var (subPath, version) = DetectActiveSubPath(containerDir);
            if (subPath is null && !HasGameFolder(containerDir)) continue;

            found.Add(new RenPyGame
            {
                Name = Path.GetFileName(containerDir),
                ContainerPath = containerDir,
                ActiveSubPath = subPath,
                LocalVersion = version,
            });
        }
        return found;
    }

    /// <summary>Wie <see cref="Scan"/>, aber für einen einzelnen Container-
    /// Ordner (z. B. wenn der DownloadWatcher einen neuen entdeckt hat).</summary>
    public static RenPyGame? DetectOne(string containerDir)
    {
        if (!Directory.Exists(containerDir)) return null;
        var (subPath, version) = DetectActiveSubPath(containerDir);
        if (subPath is null && !HasGameFolder(containerDir)) return null;
        return new RenPyGame
        {
            Name = Path.GetFileName(containerDir),
            ContainerPath = containerDir,
            ActiveSubPath = subPath,
            LocalVersion = version,
        };
    }

    /// <summary>Findet den aktiven Sub-Path und dessen Version. Rückgabe:
    /// (null, null) = kein Ren'Py-Spiel; (subPath, version) = Sub-Path mit
    /// game/-Marker gefunden; ("", version) = Legacy-Layout, Container ist
    /// selbst das Spiel.</summary>
    private static (string? SubPath, string? Version) DetectActiveSubPath(string containerDir)
    {
        // Prio 1: direktes game/ im Container → Legacy-Layout
        if (HasGameFolder(containerDir))
        {
            var v = ExtractVersion(Path.GetFileName(containerDir));
            return (null, v);
        }

        // Prio 2+3: alle Sub-Ordner mit game/, sortiert nach Version
        var candidates = Directory.EnumerateDirectories(containerDir)
            .Where(HasGameFolder)
            .Select(d => new { Dir = d, Version = ExtractVersion(Path.GetFileName(d)) })
            .OrderByDescending(x => x.Version ?? "", System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0) return (null, null);
        var top = candidates[0];
        return (Path.GetFileName(top.Dir), top.Version);
    }

    private static bool HasGameFolder(string dir)
    {
        try { return Directory.Exists(Path.Combine(dir, "game")); }
        catch { return false; }
    }

    public static string? ExtractVersion(string folderName)
    {
        var m = VersionInFolderRegex.Match(folderName);
        return m.Success ? m.Groups[1].Value : null;
    }
}
