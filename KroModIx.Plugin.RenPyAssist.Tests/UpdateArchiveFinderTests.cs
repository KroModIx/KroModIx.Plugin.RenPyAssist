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
    public void RAR_und_7z_zaehlen_seit_v0_21_mit()
    {
        // Der Installer entpackt seit v0.21.0 ueber SharpCompress, also darf
        // die Suche sie auch vorschlagen. Vorher waeren das Treffer gewesen,
        // die erst nach der Bestaetigung beim Entpacken scheitern.
        Touch("HeavenlyVows-0.9.0-pc.rar");
        Touch("HeavenlyVows-0.9.0-pc.7z");
        var hits = UpdateArchiveFinder.Find(_dir, "Heavenly Vows", "0.8.0", "0.9.0");
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void Fremde_Formate_bleiben_draussen()
    {
        // Was der Installer nicht oeffnen kann, darf auch nicht vorgeschlagen
        // werden — sonst bestaetigt der User einen Treffer, der scheitert.
        Touch("HeavenlyVows-0.9.0-pc.exe");
        Touch("HeavenlyVows-0.9.0-pc.tar.gz");
        Touch("HeavenlyVows-0.9.0-pc.apk");
        var hits = UpdateArchiveFinder.Find(_dir, "Heavenly Vows", "0.8.0", "0.9.0");
        Assert.Empty(hits);
    }

    [Fact]
    public void Suche_und_Installer_kennen_dieselben_Formate()
    {
        // Regressions-Netz: waechst die eine Liste, muss die andere mit.
        Assert.Equal(
            new[] { ".7z", ".rar", ".zip" },
            UpdateArchiveFinder.SupportedExtensions.OrderBy(e => e, StringComparer.Ordinal).ToArray());
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
