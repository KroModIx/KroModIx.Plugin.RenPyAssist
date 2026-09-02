using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Sucht im Downloads-Ordner nach einem passenden Update-Archiv für
/// ein Spiel (v0.20.0).
///
/// <para>Hintergrund: Ren'Py-Updates kommen als ZIP von f95zone und landen
/// im Browser-Download-Ordner. Bisher musste der User beim „Update
/// installieren" jedes Mal durch den Datei-Dialog navigieren, obwohl die
/// Datei fast immer frisch heruntergeladen dort liegt und ihr Name das Spiel
/// samt Version enthält.</para>
///
/// <para>ZIP, RAR und 7z — dieselben Formate, die der
/// <see cref="GameUpdateInstaller"/> seit v0.21.0 entpacken kann. Die beiden
/// Listen muessen zusammenpassen: ein Vorschlag, den der Installer nicht
/// oeffnen kann, scheitert erst nach der Bestaetigung des Users.</para></summary>
public static class UpdateArchiveFinder
{
    public static readonly string[] SupportedExtensions = [".zip", ".rar", ".7z"];

    /// <summary>Kandidaten im Ordner, bester zuerst. Leere Liste wenn der
    /// Ordner fehlt oder nichts plausibel passt.</summary>
    public static IReadOnlyList<UpdateArchiveCandidate> Find(
        string? downloadsDir, string gameName, string? localVersion, string? remoteVersion)
    {
        if (string.IsNullOrWhiteSpace(downloadsDir) || !Directory.Exists(downloadsDir))
            return Array.Empty<UpdateArchiveCandidate>();

        string[] files;
        try
        {
            files = Directory.GetFiles(downloadsDir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }
        catch { return Array.Empty<UpdateArchiveCandidate>(); }

        var gameKey = Normalize(gameName);
        var gameTokens = Tokenize(gameName);
        if (gameKey.Length < 3 || gameTokens.Count == 0)
            return Array.Empty<UpdateArchiveCandidate>();

        var result = new List<UpdateArchiveCandidate>();
        foreach (var file in files)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var fileKey = Normalize(stem);
            var fileTokens = Tokenize(stem);

            // Namens-Bezug: entweder der ganze Spielname steckt im Dateinamen
            // (bzw. umgekehrt), oder genug einzelne Wörter überschneiden sich.
            bool substring = fileKey.Contains(gameKey, StringComparison.Ordinal)
                             || gameKey.Contains(fileKey, StringComparison.Ordinal);
            int overlap = gameTokens.Count(t => fileTokens.Contains(t));
            double overlapRatio = (double)overlap / gameTokens.Count;
            if (!substring && overlapRatio < 0.6) continue;

            var version = RenPyGameDetector.ExtractVersion(stem);

            // Aelter oder gleich der lokalen Version? Dann ist es kein Update,
            // sondern das Archiv der Fassung, die schon installiert ist.
            if (version is not null && localVersion is not null
                && VersionCompare.Compare(version, localVersion) is int cmp && cmp <= 0)
                continue;

            int score = 0;
            if (substring) score += 25;
            score += (int)(overlapRatio * 20);
            if (version is not null)
            {
                if (remoteVersion is not null
                    && VersionCompare.Compare(version, remoteVersion) == 0) score += 100;
                else if (localVersion is not null && VersionCompare.IsRemoteNewer(version, localVersion)) score += 50;
                else score += 10;
            }

            DateTime written;
            try { written = File.GetLastWriteTimeUtc(file); } catch { written = DateTime.MinValue; }
            result.Add(new UpdateArchiveCandidate(file, Path.GetFileName(file), version, score, written));
        }

        return result
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.LastWriteUtc)
            .ToList();
    }

    /// <summary>Nur Buchstaben und Ziffern, lowercase — damit
    /// „A Wife's Loyalty" und „AWifesLoyalty-0.9-pc" zueinander finden.</summary>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        Span<char> buffer = stackalloc char[value.Length];
        int n = 0;
        foreach (var c in value)
            if (char.IsLetterOrDigit(c)) buffer[n++] = char.ToLowerInvariant(c);
        return new string(buffer[..n]);
    }

    /// <summary>Wort-Tokens ab drei Zeichen, ohne die üblichen Verpackungs-
    /// Wörter aus Release-Dateinamen.</summary>
    internal static HashSet<string> Tokenize(string? value)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) return set;
        foreach (var raw in value.Split(
                     [' ', '-', '_', '.', '(', ')', '[', ']', '+', '\''],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var token = Normalize(raw);
            if (token.Length < 3) continue;
            if (NoiseWords.Contains(token)) continue;
            set.Add(token);
        }
        return set;
    }

    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "pc", "win", "windows", "linux", "mac", "osx", "android", "all",
        "final", "full", "public", "compressed", "part", "the", "and",
        "game", "update", "version", "renpy", "zip",
    };
}

/// <summary>Ein gefundenes Archiv. <see cref="Score"/> ist nur intern zum
/// Sortieren gedacht — die UI zeigt Dateiname und erkannte Version.</summary>
public sealed record UpdateArchiveCandidate(
    string FullPath,
    string FileName,
    string? Version,
    int Score,
    DateTime LastWriteUtc);
