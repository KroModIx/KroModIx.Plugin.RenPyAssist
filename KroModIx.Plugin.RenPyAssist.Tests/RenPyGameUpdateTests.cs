using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Der Bug wie er beim User ankam: die Registry-Zeile aus
/// <c>games.json</c> darf kein Update-Badge erzeugen.</summary>
public sealed class RenPyGameUpdateTests
{
    [Fact]
    public void SteamCity_lokal_neuer_als_Thread_meldet_kein_Update()
    {
        var game = new RenPyGame
        {
            Name = "SteamCity",
            ActiveSubPath = "SteamCity-0.5-pc",
            LocalVersion = "0.5",
            LastRemoteVersion = "0.4.5",
            ThreadUrl = "https://f95zone.to/threads/example.1/",
        };
        Assert.False(game.HasUpdate);
    }

    [Fact]
    public void Echtes_Update_meldet_weiterhin()
    {
        var game = new RenPyGame { LocalVersion = "0.4.5", LastRemoteVersion = "0.5" };
        Assert.True(game.HasUpdate);
    }

    [Fact]
    public void Ohne_Thread_Version_kein_Update()
    {
        var game = new RenPyGame { LocalVersion = "0.5", LastRemoteVersion = null };
        Assert.False(game.HasUpdate);
    }
}
