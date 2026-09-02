using System;
using System.IO;
using System.Linq;
using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Die Datei-Suche hinter dem Update-Badge. Der teure Fehler waere
/// hier ein Fehlgriff: das falsche Archiv wird kommentarlos ueber ein Spiel
/// installiert.</summary>
public sealed class UpdateArchiveFinderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "renpy-finder-" + Guid.NewGuid().ToString("N"));

    public UpdateArchiveFinderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void Touch(string name) => File.WriteAllText(Path.Combine(_dir, name), "x");

    [Fact]
    public void Findet_das_Archiv_mit_der_Remote_Version()
    {
        Touch("HeavenlyVows-0.9.0-pc.zip");
        Touch("HeavenlyVows-0.8.0-pc.zip");
        Touch("irgendwas-anderes-1.0.zip");

        var hits = UpdateArchiveFinder.Find(_dir, "Heavenly Vows", "0.8.0", "0.9.0");

        Assert.NotEmpty(hits);
        Assert.Equal("HeavenlyVows-0.9.0-pc.zip", hits[0].FileName);
        Assert.Equal("0.9.0", hits[0].Version);
    }

    [Fact]
    public void Ignoriert_die_bereits_installierte_Version()
    {
        Touch("HeavenlyVows-0.8.0-pc.zip");
        var hits = UpdateArchiveFinder.Find(_dir, "Heavenly Vows", "0.8.0", "0.9.0");
        Assert.Empty(hits);
    }

    [Fact]
    public void Ignoriert_aeltere_Versionen()
    {
        Touch("HeavenlyVows-0.7.0-pc.zip");
        var hits = UpdateArchiveFinder.Find(_dir, "Heavenly Vows", "0.8.0", "0.9.0");
        Assert.Empty(hits);
    }

    [Fact]
    public void Fremde_Spiele_matchen_nicht()
    {
        Touch("CompletelyDifferentGame-1.2.zip");
        Touch("Boundaries-of-Morality-0.9.zip");
        var hits = UpdateArchiveFinder.Find(_dir, "Heavenly Vows", "0.8.0", "0.9.0");
        Assert.Empty(hits);
    }

    [Theory]
    [InlineData("A Wife's Loyalty", "AWifesLoyalty-0.9-pc.zip")]
    [InlineData("Guilty Pleasure", "Guilty_Pleasure_0.9_pc.zip")]
    [InlineData("In the shadows of Ashwood", "In-the-shadows-of-Ashwood-0.9-pc.zip")]
    public void Findet_ueber_Schreibweisen_hinweg(string gameName, string file)
    {
        Touch(file);
        var hits = UpdateArchiveFinder.Find(_dir, gameName, "0.8", "0.9");
        Assert.Single(hits);
        Assert.Equal(file, hits[0].FileName);
    }

    [Fact]
    public void Nur_ZIP_denn_der_Installer_kann_nur_ZIP()
    {
        Touch("HeavenlyVows-0.9.0-pc.rar");
        Touch("HeavenlyVows-0.9.0-pc.7z");
        var hits = UpdateArchiveFinder.Find(_dir, "Heavenly Vows", "0.8.0", "0.9.0");
        Assert.Empty(hits);
    }

    [Fact]
    public void Ohne_Ordner_keine_Treffer_und_kein_Fehler()
    {
        Assert.Empty(UpdateArchiveFinder.Find(null, "Heavenly Vows", "0.8.0", "0.9.0"));
        Assert.Empty(UpdateArchiveFinder.Find("/gibt/es/nicht", "Heavenly Vows", "0.8.0", "0.9.0"));
    }

    [Fact]
    public void Ohne_erkannte_Version_bleibt_der_Treffer_moeglich()
    {
        // Manche Releases haben keine Version im Dateinamen — dann entscheidet
        // der Name, und der User bestaetigt im Dialog.
        Touch("HeavenlyVows-hotfix.zip");
        var hits = UpdateArchiveFinder.Find(_dir, "Heavenly Vows", "0.8.0", "0.9.0");
        Assert.Single(hits);
        Assert.Null(hits[0].Version);
    }

    [Fact]
    public void Realer_Fall_aus_dem_Downloads_Ordner()
    {
        // Genau der Dateiname, der beim User lag, als er sich den Badge-Klick
        // gewuenscht hat: Spielname mit Unterstrichen, Kuerzel dahinter,
        // v-Prefix an der Version, "Public" und "pc" als Rauschen.
        Touch("Heavenly_Vows_HT-v0.9.0-Public-pc.zip");
        Touch("WifeTrainerFiles-0.7s-pc.zip");

        var hits = UpdateArchiveFinder.Find(_dir, "Heavenly Vows", "0.8.0", "0.9.0");

        Assert.Single(hits);
        Assert.Equal("Heavenly_Vows_HT-v0.9.0-Public-pc.zip", hits[0].FileName);
        Assert.Equal("0.9.0", hits[0].Version);
    }

    [Fact]
    public void Bestes_Match_steht_vorn()
    {
        Touch("HeavenlyVows-0.9.0-pc.zip");
        Touch("HeavenlyVows-1.0-fanmod.zip");
        var hits = UpdateArchiveFinder.Find(_dir, "Heavenly Vows", "0.8.0", "0.9.0");
        Assert.Equal("HeavenlyVows-0.9.0-pc.zip", hits[0].FileName);
        Assert.Equal(2, hits.Count);
    }
}
