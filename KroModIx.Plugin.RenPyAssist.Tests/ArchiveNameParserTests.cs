using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Namensvorschlag aus dem Archivnamen. Gemessen an den 22 Archiven,
/// die hier real neben den Spielen liegen: 15 Treffer. Der Rest ist nicht
/// mechanisch ableitbar — deshalb ist das Ergebnis ein vorbefuelltes
/// Eingabefeld, keine Festlegung.</summary>
public class ArchiveNameParserTests
{
    [Theory]
    [InlineData("TheWifeSecret-0.3-pc.zip", "The Wife Secret")]
    [InlineData("LeafFlow-v0.06-pc.zip", "Leaf Flow")]
    [InlineData("Boundaries_of_Morality-0.720-pc.zip", "Boundaries of Morality")]
    [InlineData("Happy_Summer_-1.0-pc.zip", "Happy Summer")]
    [InlineData("CheatingWitches-0.1-public-pc.zip", "Cheating Witches")]
    [InlineData("Whispers_of_Desire-0.7-pc.zip", "Whispers of Desire")]
    [InlineData("StreetsOfSorcery-1.11b-pc.zip", "Streets Of Sorcery")]
    [InlineData("BetweenUs-0.8.1-pc.zip", "Between Us")]
    public void Trifft_die_gaengigen_Namensmuster(string datei, string erwartet)
        => Assert.Equal(erwartet, ArchiveNameParser.SuggestName(datei));

    [Theory]
    [InlineData("Furry_Tales-Ch.8.98.95-pc.zip", "Furry Tales")]
    [InlineData("StepsOfDebaucheryEpisode5.2-patreon-pc.zip", "Steps Of Debauchery")]
    [InlineData("JimmyAwkwardAdventures-Update28.v0.41.2-pc.zip", "Jimmy Awkward Adventures")]
    public void Schneidet_Versions_Praefixe_wie_Ch_Episode_Update_ab(string datei, string erwartet)
        => Assert.Equal(erwartet, ArchiveNameParser.SuggestName(datei));

    [Fact]
    public void Part_im_Titel_bleibt_stehen()
    {
        // "part" ist bewusst KEIN Versions-Marker: zwei Spiele hier tragen es
        // im Namen. Lieber ein Wort zu viel im Vorschlag als ein
        // abgeschnittener Titel.
        Assert.Equal("AWifes Life Part2", ArchiveNameParser.SuggestName("AWifesLifePart2-0.1.4-pc.zip"));
    }

    [Theory]
    [InlineData("Game-1.0-linux.zip")]
    [InlineData("Game-1.0-pc.rar")]
    [InlineData("Game-1.0-win.7z")]
    public void Plattform_Suffixe_fallen_weg(string datei)
        => Assert.Equal("Game", ArchiveNameParser.SuggestName(datei));

    [Fact]
    public void Versalien_Abkuerzungen_bleiben_zusammen()
        => Assert.Equal("Lisa TI", ArchiveNameParser.SuggestName("Lisa-TI-1.0-pc.zip"));

    [Fact]
    public void Ohne_erkennbare_Wortgrenze_bleibt_der_Name_am_Stueck()
    {
        // "Shadowsofseduction" heisst in Wahrheit "Shadows of seduction".
        // Wo die Grenzen liegen, weiss nur ein Mensch — der Vorschlag raet
        // nicht, er liefert das Ausgangsmaterial.
        Assert.Equal("Shadowsofseduction",
            ArchiveNameParser.SuggestName("Shadowsofseduction-0.5.08013-pc.zip"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Leere_Eingabe_liefert_leer(string datei)
        => Assert.Equal("", ArchiveNameParser.SuggestName(datei));

    [Fact]
    public void Voller_Pfad_wird_wie_der_Dateiname_behandelt()
        => Assert.Equal("Between Us",
            ArchiveNameParser.SuggestName("/home/x/Downloads/BetweenUs-0.8.1-pc.zip"));

    [Fact]
    public void Build_Ordnername_funktioniert_genauso()
        => Assert.Equal("Happy Summer", ArchiveNameParser.SuggestName("Happy_Summer-1.0-pc"));
}
