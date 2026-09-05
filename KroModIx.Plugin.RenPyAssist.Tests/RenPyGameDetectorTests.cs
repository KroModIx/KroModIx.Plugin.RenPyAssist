using System;
using System.IO;
using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Container-Erkennung. Wenn die hier danebengreift, taucht ein
/// Spiel gar nicht erst in der Registry auf — die Kachel steht dann leer da.</summary>
public sealed class RenPyGameDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "renpy-detect-" + Guid.NewGuid().ToString("N"));

    public RenPyGameDetectorTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    /// <summary>Legt einen Container mit N Build-Sub-Ordnern an, jeder mit
    /// dem <c>game/</c>-Marker. Rueckgabe: der Container-Pfad.</summary>
    private string Container(string name, params string[] buildDirs)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var b in buildDirs)
            Directory.CreateDirectory(Path.Combine(dir, b, "game"));
        return dir;
    }

    [Fact]
    public void Linux_Build_wird_genau_wie_ein_pc_Build_erkannt()
    {
        // Der Fall aus der Praxis: reines Linux-Release, Suffix -linux statt
        // -pc. Am Detector haengt kein Plattform-Suffix, das darf auch nie
        // dazukommen — Ren'Py-Marker ist einzig der game/-Ordner.
        var dir = Container("My New Paranormal Life", "MNPL-0.6.2-linux");

        var game = RenPyGameDetector.DetectOne(dir);

        Assert.NotNull(game);
        Assert.Equal("MNPL-0.6.2-linux", game!.ActiveSubPath);
        Assert.Equal("0.6.2", game.LocalVersion);
    }

    [Theory]
    [InlineData("Game-1.2.3-linux", "1.2.3")]
    [InlineData("Game-1.2.3-pc", "1.2.3")]
    [InlineData("Game-1.2.3-mac", "1.2.3")]
    [InlineData("MNPL-0.6.2-linux", "0.6.2")]
    public void Version_kommt_unabhaengig_vom_Plattform_Suffix_an(string build, string expected)
    {
        var game = RenPyGameDetector.DetectOne(Container("C-" + build, build));
        Assert.Equal(expected, game!.LocalVersion);
    }

    [Fact]
    public void Legacy_Layout_Container_ist_selbst_das_Spiel()
    {
        var dir = Path.Combine(_root, "AltesSpiel-0.9-pc");
        Directory.CreateDirectory(Path.Combine(dir, "game"));

        var game = RenPyGameDetector.DetectOne(dir);

        Assert.NotNull(game);
        Assert.Null(game!.ActiveSubPath);
        Assert.Equal("0.9", game.LocalVersion);
    }

    [Fact]
    public void Bei_mehreren_Builds_gewinnt_die_hoechste_Version_semantisch()
    {
        var dir = Container("Rotation", "Game-0.9-linux", "Game-0.10-linux");

        var game = RenPyGameDetector.DetectOne(dir);

        Assert.Equal("Game-0.10-linux", game!.ActiveSubPath);
        Assert.Equal("0.10", game.LocalVersion);
    }

    [Fact]
    public void Ordner_ohne_game_Marker_ist_kein_RenPy_Spiel()
    {
        var dir = Path.Combine(_root, "Unassigned-Downloads");
        Directory.CreateDirectory(Path.Combine(dir, "archive"));

        Assert.Null(RenPyGameDetector.DetectOne(dir));
    }

    [Fact]
    public void Archive_Ordner_neben_dem_Build_stoert_die_Wahl_nicht()
    {
        var dir = Container("Mit Archiv", "MNPL-0.6.2-linux");
        Directory.CreateDirectory(Path.Combine(dir, "Archive"));

        Assert.Equal("MNPL-0.6.2-linux", RenPyGameDetector.DetectOne(dir)!.ActiveSubPath);
    }

    [Fact]
    public void Scan_liefert_alle_Container_des_Roots()
    {
        Container("Spiel A", "A-1.0-linux");
        Container("Spiel B", "B-2.0-pc");
        Directory.CreateDirectory(Path.Combine(_root, "Temp"));

        var found = RenPyGameDetector.Scan(_root);

        Assert.Equal(2, found.Count);
    }
}
