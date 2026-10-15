using System;
using System.IO;
using System.Linq;
using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Spiele-Root-Ableitung und Ordnernamen-Bereinigung. Beides
/// entscheidet, wo auf der Platte geschrieben wird — ein Fehlgriff legt den
/// Container an der falschen Stelle an.</summary>
public sealed class GameInstallServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "renpy-install-" + Guid.NewGuid().ToString("N"));

    public GameInstallServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private RenPyGame Game(string relContainer)
    {
        var p = Path.Combine(_dir, Path.Combine(relContainer.Split('/')));
        Directory.CreateDirectory(p);
        return new RenPyGame { Name = Path.GetFileName(p), ContainerPath = p };
    }

    [Fact]
    public void Root_ist_das_haeufigste_Elternverzeichnis()
    {
        var spiele = new[] { Game("Games/A"), Game("Games/B"), Game("Woanders/C") };
        Assert.Equal(Path.Combine(_dir, "Games"),
            GameInstallService.GuessGamesRoot(spiele));
    }

    [Fact]
    public void Bei_Gleichstand_entscheidet_eine_feste_Reihenfolge()
    {
        // Ohne diese Regel haengt das Ergebnis an der Aufzaehlungsreihenfolge
        // und das Spiel landet mal hier, mal dort.
        var spiele = new[] { Game("BBB/x"), Game("AAA/y") };
        var a = GameInstallService.GuessGamesRoot(spiele);
        var b = GameInstallService.GuessGamesRoot(spiele.Reverse().ToArray());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Ohne_Spiele_gibt_es_keinen_Root()
        => Assert.Null(GameInstallService.GuessGamesRoot(Array.Empty<RenPyGame>()));

    [Fact]
    public void Nicht_mehr_vorhandene_Ordner_zaehlen_nicht_mit()
    {
        var echt = Game("Games/A");
        var weg = new RenPyGame { Name = "weg", ContainerPath = "/gibt/es/nicht/X" };
        Assert.Equal(Path.Combine(_dir, "Games"),
            GameInstallService.GuessGamesRoot(new[] { echt, weg }));
    }

    [Theory]
    [InlineData("A Wife's Loyalty", "A Wife's Loyalty")]
    [InlineData("Lisa: TI", "Lisa TI")]
    [InlineData("Game/With/Slashes", "Game With Slashes")]
    [InlineData("  Viele   Leerzeichen  ", "Viele Leerzeichen")]
    [InlineData("Punkt am Ende.", "Punkt am Ende")]
    public void Ordnername_wird_bereinigt(string eingabe, string erwartet)
        => Assert.Equal(erwartet, GameInstallService.SanitizeFolderName(eingabe));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    public void Unbrauchbarer_Name_liefert_leer(string eingabe)
        => Assert.Equal("", GameInstallService.SanitizeFolderName(eingabe));
}
