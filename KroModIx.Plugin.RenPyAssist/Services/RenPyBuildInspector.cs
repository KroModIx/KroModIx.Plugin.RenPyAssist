using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Was ein Build-Ordner mitbringt — und was ihm fehlt.</summary>
/// <param name="HasLinuxRuntime">Es gibt ein <c>lib/</c>-Verzeichnis für
/// Linux (Ren'Py 7 <c>linux-*</c> oder Ren'Py 8 <c>py3-linux-*</c>).</param>
/// <param name="HasWindowsRuntime">Es gibt eine Windows-Runtime.</param>
/// <param name="RenPyVersion">Die drei ersten Stellen, z. B. <c>8.5.2</c> —
/// das ist, was die SDK-URL braucht. Null, wenn nicht lesbar.</param>
/// <param name="GameScriptName">Der Name der <c>.py</c> im Build-Ordner, ohne
/// Endung. Danach müssen Startskript und Binary heißen.</param>
public sealed record BuildInspection(
    bool HasLinuxRuntime, bool HasWindowsRuntime,
    string? RenPyVersion, string? GameScriptName)
{
    /// <summary>Ein Windows-only-Build: spielbar wäre er, die Linux-Runtime
    /// fehlt aber ganz. Nicht zu verwechseln mit dem Fall, den
    /// <see cref="ExecutableBits"/> behandelt — dort ist die Runtime da und
    /// hat nur keine Exec-Bits.</summary>
    public bool IsWindowsOnly => !HasLinuxRuntime && HasWindowsRuntime;

    /// <summary>Alles beisammen, um die Runtime nachzurüsten.</summary>
    public bool CanBeFixed => IsWindowsOnly
        && !string.IsNullOrWhiteSpace(RenPyVersion)
        && !string.IsNullOrWhiteSpace(GameScriptName);
}

/// <summary>Sieht einem Ren'Py-Build an, ob er unter Linux starten kann.
///
/// <para>Anlass war ein Build, der über KroModIx installiert wurde und sich
/// nicht starten ließ: <c>lib/</c> enthielt nur <c>py3-windows-x86_64</c>.
/// <see cref="ExecutableBits"/> greift dort nicht — es repariert fehlende
/// Exec-Bits an einer vorhandenen Runtime, hier fehlte die Runtime
/// selbst.</para></summary>
public static class RenPyBuildInspector
{
    /// <summary><c>version = '8.5.2.26010301'</c> in
    /// <c>renpy/vc_version.py</c>. Für die SDK-URL zählen die ersten drei
    /// Stellen.</summary>
    private static readonly Regex VcVersion = new(
        @"^\s*version\s*=\s*['""](?<v>\d+\.\d+\.\d+)", 
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary><c>version_tuple = (8, 5, 2, vc_version)</c> in
    /// <c>renpy/__init__.py</c> — die Zweitquelle.</summary>
    private static readonly Regex VersionTuple = new(
        @"version_tuple\s*=\s*\(\s*(?<a>\d+)\s*,\s*(?<b>\d+)\s*,\s*(?<c>\d+)",
        RegexOptions.Compiled);

    public static BuildInspection Inspect(string buildDir)
    {
        if (string.IsNullOrWhiteSpace(buildDir) || !Directory.Exists(buildDir))
            return new BuildInspection(false, false, null, null);

        bool linux = false, windows = false;
        var libDir = Path.Combine(buildDir, "lib");
        if (Directory.Exists(libDir))
        {
            try
            {
                foreach (var d in Directory.EnumerateDirectories(libDir))
                {
                    var name = Path.GetFileName(d);
                    if (IsLinuxPlatformDir(name)) linux = true;
                    else if (IsWindowsPlatformDir(name)) windows = true;
                }
            }
            catch (Exception) { /* unlesbar = nichts gefunden */ }
        }

        return new BuildInspection(linux, windows,
            ReadRenPyVersion(buildDir), FindGameScriptName(buildDir));
    }

    /// <summary>Dieselbe Regel wie in <see cref="ExecutableBits"/> — hier
    /// gebraucht, um ihr Gegenteil festzustellen.</summary>
    public static bool IsLinuxPlatformDir(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.StartsWith("linux-", StringComparison.OrdinalIgnoreCase)) return true;
        return name.StartsWith("py", StringComparison.OrdinalIgnoreCase)
               && name.Contains("-linux-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWindowsPlatformDir(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.StartsWith("windows-", StringComparison.OrdinalIgnoreCase)) return true;
        return name.StartsWith("py", StringComparison.OrdinalIgnoreCase)
               && name.Contains("-windows-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Liest die Ren'Py-Version aus dem Build. Erst
    /// <c>renpy/vc_version.py</c>, dann <c>renpy/__init__.py</c>.
    /// Geraten wird nicht — die Runtime muss exakt zur Version passen.</summary>
    public static string? ReadRenPyVersion(string buildDir)
    {
        var vc = Path.Combine(buildDir, "renpy", "vc_version.py");
        var treffer = TryMatch(vc, VcVersion, m => m.Groups["v"].Value);
        if (treffer is not null) return treffer;

        var init = Path.Combine(buildDir, "renpy", "__init__.py");
        return TryMatch(init, VersionTuple,
            m => $"{m.Groups["a"].Value}.{m.Groups["b"].Value}.{m.Groups["c"].Value}");
    }

    private static string? TryMatch(string file, Regex regex, Func<Match, string> pick)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var m = regex.Match(File.ReadAllText(file));
            return m.Success ? pick(m) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Der Name der <c>.py</c> im Build-Ordner, ohne Endung.
    ///
    /// <para>Danach müssen Startskript und Binary heißen — <b>nicht</b> nach
    /// dem Ordner. Im Anlassfall hieß der Ordner
    /// <c>goodluke_thegame-1.2(WIP)-win</c>, die Datei aber
    /// <c>goodluke_thegame.py</c>; das Binary leitet den Namen seiner
    /// <c>.py</c> aus seinem eigenen Dateinamen ab.</para></summary>
    public static string? FindGameScriptName(string buildDir)
    {
        try
        {
            var kandidaten = Directory.EnumerateFiles(buildDir, "*.py")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .Where(n => !string.Equals(n, "renpy", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            return kandidaten.FirstOrDefault();
        }
        catch (Exception) { return null; }
    }
}
