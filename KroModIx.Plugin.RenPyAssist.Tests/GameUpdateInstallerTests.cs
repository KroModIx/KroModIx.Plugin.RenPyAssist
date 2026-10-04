using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using KroModIx.Plugin.RenPyAssist.Services;
using KroModIx.Plugin.TestKit;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Der Einbau eines Update-Archivs. Geprüft wird die Entscheidung
/// des Plugins, nicht das Auspacken selbst — das macht seit v0.23.0 der
/// Host-Baukasten.
///
/// <para><b>Was sich geändert hat:</b> der eigene Ausbruch-Schutz war
/// richtig gerechnet, <b>übersprang</b> den abgelehnten Eintrag aber still
/// und ließ den Einbau weiterlaufen. Das war hier schlimmer als in den
/// anderen Plugins: die Schritte danach arbeiteten auf einem halb entpackten
/// Stand, und am Ende wird der <b>alte Unterordner samt Spielständen
/// gelöscht</b>. Jetzt bricht der Einbau ab, bevor irgendetwas am alten
/// Stand passiert.</para></summary>
public sealed class GameUpdateInstallerTests : IDisposable
{
    private readonly string _tmp;
    private readonly string _container;
    private readonly FakeHostServices _host;
    private readonly FakeArchiveService _archives = new();
    private readonly GameUpdateInstaller _sut;
    private readonly RenPyGame _game;

    public GameUpdateInstallerTests()
    {
        _tmp = Directory.CreateTempSubdirectory("kromodix-renpy-upd").FullName;
        _container = Path.Combine(_tmp, "MeinSpiel");
        Directory.CreateDirectory(_container);

        _host = new FakeHostServices(Path.Combine(_tmp, "host")) { Archives = _archives };
        _sut = new GameUpdateInstaller(new GamesRegistry(new RenPyPaths(_host)), _archives);

        // Der alte Stand: ein Unterordner mit Spielstaenden, die der Einbau
        // uebernehmen soll.
        var alt = Path.Combine(_container, "MeinSpiel-0.8.0-pc");
        Directory.CreateDirectory(Path.Combine(alt, "game", "saves"));
        File.WriteAllText(Path.Combine(alt, "game", "saves", "1-1-LT1.save"), "Spielstand");
        _game = new RenPyGame
        {
            Name = "MeinSpiel",
            ContainerPath = _container,
            ActiveSubPath = "MeinSpiel-0.8.0-pc",
            LocalVersion = "0.8.0",
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    private string BuildZip(string name, params string[] eintraege)
    {
        var zipPath = Path.Combine(_tmp, name);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var pfad in eintraege)
        {
            using var s = archive.CreateEntry(pfad).Open();
            s.Write([1, 2, 3]);
        }
        return zipPath;
    }

    [Fact]
    public async Task Archiv_mit_Versions_Unterordner_wird_eingebaut()
    {
        var zip = BuildZip("MeinSpiel-0.9.0-pc.zip",
            "MeinSpiel-0.9.0-pc/game/script.rpa",
            "MeinSpiel-0.9.0-pc/MeinSpiel.exe");

        var r = await _sut.InstallAsync(_game, zip, TestContext.Current.CancellationToken);

        Assert.True(r.Success, r.Error ?? "");
        Assert.True(File.Exists(
            Path.Combine(_container, "MeinSpiel-0.9.0-pc", "game", "script.rpa")));
        // Die Spielstaende muessen mitwandern.
        Assert.True(File.Exists(Path.Combine(_container, "MeinSpiel-0.9.0-pc",
            "game", "saves", "1-1-LT1.save")));
    }

    /// <summary>Ein flaches Archiv (<c>game/</c> direkt) wird in einen
    /// Unterordner mit dem Namen der Archiv-Datei gehüllt.</summary>
    [Fact]
    public async Task Flaches_Archiv_wird_in_einen_Unterordner_gehuellt()
    {
        var zip = BuildZip("MeinSpiel-0.9.0-pc.zip",
            "game/script.rpa", "MeinSpiel.exe");

        var r = await _sut.InstallAsync(_game, zip, TestContext.Current.CancellationToken);

        Assert.True(r.Success, r.Error ?? "");
        Assert.True(File.Exists(
            Path.Combine(_container, "MeinSpiel-0.9.0-pc", "game", "script.rpa")));
    }

    /// <summary>Der Kern der Änderung: ein Ausbruchsversuch bricht ab, und
    /// der alte Stand samt Spielständen bleibt stehen. Vorher lief der
    /// Einbau weiter und hätte ihn am Ende gelöscht.</summary>
    [Fact]
    public async Task Ausbruchsversuch_bricht_ab_und_laesst_den_alten_Stand_stehen()
    {
        var opfer = Path.Combine(_tmp, "ausserhalb.txt");
        var zip = BuildZip("MeinSpiel-0.9.0-pc.zip",
            "MeinSpiel-0.9.0-pc/game/script.rpa", opfer);

        var r = await _sut.InstallAsync(_game, zip, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(opfer));
        Assert.False(r.Success);
        Assert.Contains("herausschreiben", r.Error);
        Assert.Contains("unangetastet", r.Error);
        // Der alte Stand darf nicht angefasst werden.
        Assert.True(File.Exists(Path.Combine(_container, "MeinSpiel-0.8.0-pc",
            "game", "saves", "1-1-LT1.save")));
    }

    [Fact]
    public async Task Punkt_Punkt_Pfad_bricht_ebenfalls_ab()
    {
        var zip = BuildZip("MeinSpiel-0.9.0-pc.zip",
            "MeinSpiel-0.9.0-pc/game/script.rpa",
            "MeinSpiel-0.9.0-pc/../../../evil.txt");

        var r = await _sut.InstallAsync(_game, zip, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(Path.Combine(_tmp, "evil.txt")));
        Assert.False(r.Success);
    }

    /// <summary>Eine Datei mit Archiv-Endung, die kein Archiv ist (ein
    /// abgebrochener Download von f95zone), wird am Inhalt erkannt — vorher
    /// scheiterte sie erst in der Archiv-Bibliothek, mit deren Meldung.</summary>
    [Fact]
    public async Task Kein_Archiv_wird_am_Inhalt_erkannt()
    {
        var kaputt = Path.Combine(_tmp, "abgebrochen.zip");
        File.WriteAllText(kaputt, "das ist kein ZIP");

        var r = await _sut.InstallAsync(_game, kaputt, TestContext.Current.CancellationToken);

        Assert.False(r.Success);
        Assert.Contains("kein lesbares Archiv", r.Error);
    }

    [Fact]
    public void Endungs_Vorfilter_kommt_aus_dem_Baukasten()
    {
        Assert.Equal([".zip", ".rar", ".7z"], _sut.SupportedExtensions);
        Assert.True(_sut.HasSupportedExtension("spiel.7Z"));
        Assert.False(_sut.HasSupportedExtension("liesmich.txt"));
    }
}

/// <summary>Die destruktivste Stelle des Plugins: nach einem Einbau wird der
/// alte Versions-Unterordner <b>rekursiv</b> gelöscht, im Spielordner des
/// Nutzers, und dort liegen Spielstände.
///
/// <para>Von den zehn löschenden Pfaden dieses Plugins ist dies der einzige,
/// der überhaupt ins Verzeichnis des Nutzers greift — die anderen neun
/// betreffen Cover-Zwischenspeicher, Einstellungen, Sitzungsdaten und
/// Temp-Dateien des Plugins selbst. Deshalb bekommt nur diese eine die
/// Verweis-Prüfung.</para></summary>
public sealed class AlterOrdnerLoeschenTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("renpy-altloeschen").FullName;
    private readonly string _container;
    private readonly FakeHostServices _host;
    private readonly GameUpdateInstaller _sut;

    public AlterOrdnerLoeschenTests()
    {
        _container = Path.Combine(_tmp, "MeinSpiel");
        Directory.CreateDirectory(_container);
        var archives = new FakeArchiveService();
        _host = new FakeHostServices(Path.Combine(_tmp, "host")) { Archives = archives };
        _sut = new GameUpdateInstaller(new GamesRegistry(new RenPyPaths(_host)), archives);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    private string Zip(string name, params string[] eintraege)
    {
        var p = Path.Combine(_tmp, name);
        using var a = ZipFile.Open(p, ZipArchiveMode.Create);
        foreach (var e in eintraege) { using var s = a.CreateEntry(e).Open(); s.Write([1]); }
        return p;
    }

    private RenPyGame Spiel(string alterUnterordner) => new()
    {
        Name = "MeinSpiel",
        ContainerPath = _container,
        ActiveSubPath = alterUnterordner,
        LocalVersion = "0.8.0",
    };

    /// <summary>Normalfall, unverändert: ein echter alter Ordner wird nach dem
    /// Einbau gelöscht.</summary>
    [Fact]
    public async Task Ein_echter_alter_Ordner_wird_geloescht()
    {
        var alt = Path.Combine(_container, "MeinSpiel-0.8.0-pc");
        Directory.CreateDirectory(Path.Combine(alt, "game", "saves"));
        File.WriteAllText(Path.Combine(alt, "game", "saves", "1-1.save"), "Stand");

        var r = await _sut.InstallAsync(Spiel("MeinSpiel-0.8.0-pc"),
            Zip("MeinSpiel-0.9.0-pc.zip", "MeinSpiel-0.9.0-pc/game/script.rpa"),
            TestContext.Current.CancellationToken);

        Assert.True(r.Success, r.Error ?? "");
        Assert.False(Directory.Exists(alt));
    }

    /// <summary>Ist der alte Ordner ein <b>Verweis</b> — etwa weil der Nutzer
    /// seine Fassungen auf eine andere Platte legt — bleibt er stehen. Ein
    /// rekursives Löschen darauf ist nicht das, was jemand erwartet, der
    /// bewusst verlinkt hat.</summary>
    [Fact]
    public async Task Ein_verwiesener_alter_Ordner_bleibt_stehen()
    {
        var woanders = Path.Combine(_tmp, "andere-platte", "MeinSpiel-0.8.0-pc");
        Directory.CreateDirectory(Path.Combine(woanders, "game", "saves"));
        File.WriteAllText(Path.Combine(woanders, "game", "saves", "1-1.save"), "Stand");
        var link = Path.Combine(_container, "MeinSpiel-0.8.0-pc");
        Directory.CreateSymbolicLink(link, woanders);

        var r = await _sut.InstallAsync(Spiel("MeinSpiel-0.8.0-pc"),
            Zip("MeinSpiel-0.9.0-pc.zip", "MeinSpiel-0.9.0-pc/game/script.rpa"),
            TestContext.Current.CancellationToken);

        Assert.True(r.Success, r.Error ?? "");
        Assert.True(Directory.Exists(link), "der Verweis bleibt");
        Assert.True(File.Exists(Path.Combine(woanders, "game", "saves", "1-1.save")),
            "und das Ziel samt Spielstaenden erst recht");
    }
}
