using System;
using System.IO;
using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Die Sicherungen vor dem rekursiven Löschen. Das ist die
/// destruktivste Operation im Plugin — jede Bedingung hier verhindert einen
/// Fall, in dem mehr verschwindet als das eine Spiel.</summary>
public sealed class GameUninstallServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "renpy-uninstall-" + Guid.NewGuid().ToString("N"));

    public GameUninstallServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Dir(string rel)
    {
        var p = Path.Combine(_dir, Path.Combine(rel.Split('/')));
        Directory.CreateDirectory(p);
        return p;
    }

    private static string? Veto(string ziel, params string[] bekannt)
        => GameUninstallService.Veto(ziel, bekannt);

    [Fact]
    public void Unbekannter_Ordner_wird_abgelehnt()
    {
        var fremd = Dir("fremder-ordner");
        Assert.NotNull(Veto(fremd, Dir("Games/A")));
    }

    [Fact]
    public void Nicht_existierender_Ordner_wird_abgelehnt()
        => Assert.NotNull(Veto(Path.Combine(_dir, "gibtsnicht")));

    [Fact]
    public void Leerer_Pfad_wird_abgelehnt()
        => Assert.NotNull(Veto("")); 

    [Fact]
    public void Ein_Ordner_mit_weiteren_Spielen_darin_wird_abgelehnt()
    {
        // Das ist der Spiele-Hauptordner, nicht ein Spiel. Ihn zu loeschen
        // haette alle Spiele mitgenommen.
        var root = Dir("Games");
        var veto = Veto(root, root, Dir("Games/A"), Dir("Games/B"));
        Assert.NotNull(veto);
        Assert.Contains("weitere registrierte Spiele", veto);
    }

    [Fact]
    public void Ein_registriertes_einzelnes_Spiel_darf_geloescht_werden()
    {
        var a = Dir("Games/A");
        Dir("Games/B");
        Assert.Null(Veto(a, a, Dir("Games/B")));
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1536L, "1,5 KB")]
    [InlineData(4294967296L, "4 GB")]
    public void Groesse_wird_lesbar_formatiert(long bytes, string erwartet)
    {
        var s = GameUninstallService.FormatSize(bytes).Replace('.', ',');
        Assert.Equal(erwartet, s);
    }
}
