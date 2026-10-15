using System;
using System.Collections.Generic;
using System.IO;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Bestimmung des Auspack-Ziels. Ein Fehlgriff hier schreibt über
/// eine laufende Installation samt Spielständen.</summary>
public sealed class UpdateTargetResolverTests : IDisposable
{
    private readonly string _container = Path.Combine(Path.GetTempPath(),
        "renpy-target-" + Guid.NewGuid().ToString("N"));

    public UpdateTargetResolverTests() => Directory.CreateDirectory(_container);
    public void Dispose() { try { Directory.Delete(_container, true); } catch { } }

    private string Existing(string sub)
    {
        var p = Path.Combine(_container, sub, "game");
        Directory.CreateDirectory(p);
        return p;
    }

    private static UpdateTarget? Resolve(string archivName, string container,
        params string[] eintraege)
        => UpdateTargetResolver.Resolve(new FakeArchives(eintraege), archivName, container);

    [Fact]
    public void Neuer_Build_Ordner_wird_in_den_Container_entpackt()
    {
        var t = Resolve("/dl/Game-0.3-pc.zip", _container,
            "Game-0.3-pc/game/script.rpa", "Game-0.3-pc/Game.sh");

        Assert.NotNull(t);
        Assert.Equal("Game-0.3-pc", t!.SubFolderName);
        Assert.Equal(_container, t.ExtractInto);
        Assert.False(t.IsInPlace);
    }

    [Fact]
    public void Gleicher_Ordnername_wie_installiert_wird_als_In_Place_erkannt()
    {
        // Der real aufgetretene Fall: die Datei heisst v0.3, der Ordner darin
        // traegt den Namen der installierten 0.2, weil der Entwickler ihn
        // nicht umbenannt hat. Vorher fand der Diff hinterher "keinen neuen
        // Unterordner" — nachdem alle Dateien schon geschrieben waren.
        Existing("THEPLEASURESOFEGYPT-0.2-win");

        var t = Resolve("/dl/The_Pleasures_of_Egypt_v0.3-windows.zip", _container,
            "THEPLEASURESOFEGYPT-0.2-win/game/script.rpa",
            "THEPLEASURESOFEGYPT-0.2-win/THEPLEASURESOFEGYPT.exe");

        Assert.NotNull(t);
        Assert.Equal("THEPLEASURESOFEGYPT-0.2-win", t!.SubFolderName);
        Assert.True(t.IsInPlace);
    }

    [Fact]
    public void Flaches_Archiv_bekommt_einen_Ordner_aus_dem_Archivnamen()
    {
        var t = Resolve("/dl/Game-0.3-pc.zip", _container,
            "game/script.rpa", "Game.sh");

        Assert.NotNull(t);
        Assert.Equal("Game-0.3-pc", t!.SubFolderName);
        Assert.Equal(Path.Combine(_container, "Game-0.3-pc"), t.ExtractInto);
    }

    [Fact]
    public void Punkte_im_Archivnamen_ueberleben_das_Abschneiden_der_Endung()
    {
        // GetFileNameWithoutExtension haette hier ".95-pc" verschluckt.
        var t = Resolve("/dl/FT-Ch.8.98.95-pc.zip", _container, "game/script.rpa");
        Assert.Equal("FT-Ch.8.98.95-pc", t!.SubFolderName);
    }

    [Fact]
    public void Archiv_ohne_game_Ordner_liefert_null()
    {
        Assert.Null(Resolve("/dl/patch.zip", _container,
            "readme.txt", "patch/data.rpa"));
    }

    [Fact]
    public void Leeres_oder_unlesbares_Archiv_liefert_null()
    {
        Assert.Null(Resolve("/dl/leer.zip", _container));
        Assert.Null(UpdateTargetResolver.Resolve(new ThrowingArchives(), "/dl/x.zip", _container));
    }

    [Fact]
    public void Gross_Kleinschreibung_von_game_spielt_keine_Rolle()
    {
        var t = Resolve("/dl/Game-1.0-pc.zip", _container, "Game-1.0-pc/Game/script.rpa");
        Assert.Equal("Game-1.0-pc", t!.SubFolderName);
    }

    /// <summary>Nur <c>List</c> wird gebraucht; der Rest wirft, damit ein
    /// versehentlicher Zugriff im Test auffällt statt still zu passieren.</summary>
    private sealed class FakeArchives : IArchiveService
    {
        private readonly List<ArchiveEntry> _entries;
        public FakeArchives(params string[] paths)
        {
            _entries = new List<ArchiveEntry>();
            foreach (var p in paths) _entries.Add(new ArchiveEntry(p, 1));
        }
        public IReadOnlyList<string> SupportedExtensions => new[] { ".zip" };
        public bool HasSupportedExtension(string path) => true;
        public ArchiveKind DetectKind(string path) => ArchiveKind.Zip;
        public IReadOnlyList<ArchiveEntry> List(string archivePath) => _entries;
        public ArchiveExtractResult Extract(string a, string t, ArchiveExtractOptions? o = null)
            => throw new NotSupportedException("Der Resolver darf nichts auspacken.");
        public void ExtractEntry(string a, string e, string d) => throw new NotSupportedException();
        public bool TryResolveSafe(string root, string rel, out string dest)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingArchives : IArchiveService
    {
        public IReadOnlyList<string> SupportedExtensions => Array.Empty<string>();
        public bool HasSupportedExtension(string path) => false;
        public ArchiveKind DetectKind(string path) => ArchiveKind.Unknown;
        public IReadOnlyList<ArchiveEntry> List(string archivePath)
            => throw new IOException("kaputtes Archiv");
        public ArchiveExtractResult Extract(string a, string t, ArchiveExtractOptions? o = null)
            => throw new NotSupportedException();
        public void ExtractEntry(string a, string e, string d) => throw new NotSupportedException();
        public bool TryResolveSafe(string root, string rel, out string dest)
            => throw new NotSupportedException();
    }
}
