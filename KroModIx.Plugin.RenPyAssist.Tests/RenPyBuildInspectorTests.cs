using System;
using System.IO;
using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Erkennung eines Windows-only-Builds und die Angaben, die zum
/// Nachrüsten gebraucht werden. Greift das daneben, wird entweder ein
/// lauffähiges Spiel angefasst oder ein unstartbares übersehen.</summary>
public sealed class RenPyBuildInspectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "renpy-inspect-" + Guid.NewGuid().ToString("N"));

    public RenPyBuildInspectorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void Dir_(string rel) => Directory.CreateDirectory(
        Path.Combine(_dir, Path.Combine(rel.Split('/'))));

    private void File_(string rel, string content)
    {
        var p = Path.Combine(_dir, Path.Combine(rel.Split('/')));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
    }

    [Fact]
    public void Windows_only_Build_wird_erkannt()
    {
        // Die Ausgangslage des gemeldeten Spiels: lib/ hat nur Windows.
        Dir_("lib/py3-windows-x86_64");
        Dir_("lib/python3.12");
        Dir_("game");

        var b = RenPyBuildInspector.Inspect(_dir);

        Assert.True(b.IsWindowsOnly);
        Assert.False(b.HasLinuxRuntime);
        Assert.True(b.HasWindowsRuntime);
    }

    [Fact]
    public void Build_mit_Linux_Runtime_ist_nicht_betroffen()
    {
        Dir_("lib/py3-linux-x86_64");
        Dir_("lib/py3-windows-x86_64");

        Assert.False(RenPyBuildInspector.Inspect(_dir).IsWindowsOnly);
    }

    [Fact]
    public void Auch_RenPy7_Layout_zaehlt_als_Linux_Runtime()
    {
        // linux-x86_64 ohne py-Praefix — Ren'Py 7. Dieselbe Regel wie in
        // ExecutableBits, hier negiert gebraucht.
        Dir_("lib/linux-x86_64");
        Dir_("lib/windows-i686");

        Assert.False(RenPyBuildInspector.Inspect(_dir).IsWindowsOnly);
    }

    [Fact]
    public void Ohne_jede_Runtime_ist_es_kein_Windows_only_Fall()
    {
        // Nichts zum Nachruesten ableitbar — hier waere ein Eingriff Raten.
        Dir_("lib/python3.12");
        Assert.False(RenPyBuildInspector.Inspect(_dir).IsWindowsOnly);
    }

    [Fact]
    public void Version_kommt_aus_vc_version_py()
    {
        File_("renpy/vc_version.py",
            "branch = 'fix'\nnightly = False\nofficial = True\n"
            + "version = '8.5.2.26010301'\nversion_name = 'In Good Health'\n");

        Assert.Equal("8.5.2", RenPyBuildInspector.ReadRenPyVersion(_dir));
    }

    [Fact]
    public void Ohne_vc_version_zaehlt_das_version_tuple()
    {
        File_("renpy/__init__.py", "version_tuple = (8, 1, 3, vc_version)\n");
        Assert.Equal("8.1.3", RenPyBuildInspector.ReadRenPyVersion(_dir));
    }

    [Fact]
    public void Ohne_beide_Quellen_gibt_es_keine_Version()
        => Assert.Null(RenPyBuildInspector.ReadRenPyVersion(_dir));

    [Fact]
    public void Skriptname_kommt_aus_der_py_nicht_aus_dem_Ordnernamen()
    {
        // Der Fallstrick aus der Praxis: der Ordner heisst
        // "goodluke_thegame-1.2(WIP)-win", die Datei aber
        // "goodluke_thegame.py". Binary und .sh muessen zur DATEI passen.
        File_("goodluke_thegame.py", "# start");
        Assert.Equal("goodluke_thegame", RenPyBuildInspector.FindGameScriptName(_dir));
    }

    [Fact]
    public void Die_renpy_py_des_SDK_zaehlt_nicht_als_Spielskript()
    {
        File_("renpy.py", "# sdk");
        File_("meinspiel.py", "# start");
        Assert.Equal("meinspiel", RenPyBuildInspector.FindGameScriptName(_dir));
    }

    [Fact]
    public void CanBeFixed_verlangt_Version_UND_Skriptnamen()
    {
        Dir_("lib/py3-windows-x86_64");
        Assert.False(RenPyBuildInspector.Inspect(_dir).CanBeFixed);   // nichts weiter da

        File_("renpy/vc_version.py", "version = '8.5.2.1'\n");
        Assert.False(RenPyBuildInspector.Inspect(_dir).CanBeFixed);   // Skript fehlt

        File_("spiel.py", "# start");
        Assert.True(RenPyBuildInspector.Inspect(_dir).CanBeFixed);
    }

    [Fact]
    public void Der_echte_gemeldete_Build_wird_richtig_gelesen()
    {
        // Gegen den Ordner auf der Platte — wenn er da ist. Haelt fest, dass
        // die Regeln an echten Daten stimmen und nicht nur an gebauten.
        const string echt = "/run/media/system/Games/Games/goodluke thegame 1 2(WIP)"
                          + "/goodluke_thegame-1.2(WIP)-win";
        if (!Directory.Exists(echt)) return;

        var b = RenPyBuildInspector.Inspect(echt);

        Assert.Equal("8.5.2", b.RenPyVersion);
        Assert.Equal("goodluke_thegame", b.GameScriptName);
        Assert.True(b.HasWindowsRuntime);
    }
}
