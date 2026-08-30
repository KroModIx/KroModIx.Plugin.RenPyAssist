using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Regressionstests fuer den Versions-Vergleich (v0.17.1).
/// Ausloeser: lokal <c>0.5</c>, f95zone-Thread <c>0.4.5</c> — das Plugin
/// meldete ein Update, weil nur auf String-Ungleichheit geprueft wurde.</summary>
public sealed class VersionCompareTests
{
    [Theory]
    // Der gemeldete Bug: lokal ist NEUER als der Thread.
    [InlineData("0.4.5", "0.5")]
    // Gleichstand in verschiedenen Schreibweisen.
    [InlineData("0.5", "0.5")]
    [InlineData("v0.5", "0.5")]
    [InlineData("1.0", "1.0.0")]
    [InlineData("0.5", "0.5-pc")]
    [InlineData("[v0.5]", "0.5")]
    // Numerisch aelter trotz laengerem String.
    [InlineData("0.9", "0.10")]
    [InlineData("1.0", "1.0a")]
    [InlineData("1.0 beta", "1.0")]
    public void KeinUpdate_wenn_remote_nicht_neuer(string remote, string local)
        => Assert.False(VersionCompare.IsRemoteNewer(remote, local));

    [Theory]
    [InlineData("0.5", "0.4.5")]
    [InlineData("0.10", "0.9")]
    [InlineData("1.0.1", "1.0")]
    [InlineData("v0.6", "0.5")]
    [InlineData("0.4.5b", "0.4.5a")]
    [InlineData("0.4.5a", "0.4.5")]
    [InlineData("1.0", "1.0 beta")]
    public void Update_wenn_remote_echt_neuer(string remote, string local)
        => Assert.True(VersionCompare.IsRemoteNewer(remote, local));

    [Theory]
    [InlineData(null, "0.5")]
    [InlineData("0.5", null)]
    [InlineData("", "0.5")]
    [InlineData("   ", "0.5")]
    public void KeinUpdate_wenn_eine_Seite_fehlt(string? remote, string? local)
        => Assert.False(VersionCompare.IsRemoteNewer(remote, local));

    [Fact]
    public void NichtParsebar_faellt_auf_Ungleichheit_zurueck()
    {
        // Ohne numerischen Kern kann nicht sortiert werden — dann lieber
        // melden als ein echtes Update verschlucken.
        Assert.True(VersionCompare.IsRemoteNewer("Ep. 6", "Ep. 5"));
        Assert.False(VersionCompare.IsRemoteNewer("Ep. 5", "Ep. 5"));
        // Schreibweise-Unterschiede zaehlen aber auch hier nicht.
        Assert.False(VersionCompare.IsRemoteNewer("Final", "final"));
    }

    [Fact]
    public void Compare_liefert_null_wenn_nicht_parsebar()
    {
        Assert.Null(VersionCompare.Compare("Ep. 5", "0.5"));
        Assert.NotNull(VersionCompare.Compare("0.5", "0.4"));
    }

    [Fact]
    public void Comparer_sortiert_numerisch_nicht_lexikografisch()
    {
        string[] versions = ["0.9", "0.10", "0.4.5", "1.0"];
        System.Array.Sort(versions, VersionCompare.Comparer!);
        Assert.Equal(["0.4.5", "0.9", "0.10", "1.0"], versions);
    }
}
