using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Welche Versionsangabe nach einem Einbau in der Registry landet.
/// Greift das daneben, bleibt das Update-Badge nach erfolgreichem Einbau
/// stehen — oder verschwindet, obwohl nichts passiert ist.</summary>
public class UpdateVersionChoiceTests
{
    [Fact]
    public void Ordnername_gewinnt_wenn_er_die_neuere_Version_traegt()
        => Assert.Equal("0.3", GameUpdateInstaller.WähleVersion("0.3", "0.2", "0.2"));

    [Fact]
    public void Archivname_gewinnt_wenn_der_Ordner_die_alte_Version_behalten_hat()
    {
        // Der gemeldete Fall: Datei „…v0.3…zip", Ordner darin
        // „THEPLEASURESOFEGYPT-0.2-win". Ohne diese Regel uebernaehme die
        // Registry 0.2 und das Update-Badge bliebe nach dem Einbau stehen.
        Assert.Equal("0.3", GameUpdateInstaller.WähleVersion("0.2", "0.3", "0.2"));
    }

    [Fact]
    public void Ohne_Version_im_Ordnernamen_zaehlt_der_Archivname()
        => Assert.Equal("0.3", GameUpdateInstaller.WähleVersion(null, "0.3", "0.2"));

    [Fact]
    public void Ohne_Version_im_Archivnamen_zaehlt_der_Ordner()
        => Assert.Equal("0.3", GameUpdateInstaller.WähleVersion("0.3", null, "0.2"));

    [Fact]
    public void Ohne_beide_bleibt_die_bisherige_stehen()
        => Assert.Equal("0.2", GameUpdateInstaller.WähleVersion(null, null, "0.2"));

    [Fact]
    public void Semantischer_Vergleich_nicht_lexikografischer()
        => Assert.Equal("0.10", GameUpdateInstaller.WähleVersion("0.9", "0.10", "0.9"));
}
