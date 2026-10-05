using System;
using System.IO;
using System.Linq;
using KroModIx.Plugin.RenPyAssist.Services;
using Xunit;

namespace KroModIx.Plugin.RenPyAssist.Tests;

/// <summary>Zuschnitt und Wirkung der Exec-Bit-Reparatur. Greift sie zu kurz,
/// startet das Spiel nicht und niemand sieht warum; greift sie zu weit, bekommt
/// Python-Bytecode ein sinnloses +x.
///
/// <para>Die <c>Candidates</c>-Tests laufen auf jeder Plattform — sie pruefen
/// nur die Auswahl. Die <c>Apply</c>-Tests setzen echte Unix-Bits und sind
/// deshalb Linux-only: <c>File.SetUnixFileMode</c> ist anderswo nicht
/// unterstuetzt, und die CI baut auch unter Windows.</para></summary>
public sealed class ExecutableBitsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "renpy-execbits-" + Guid.NewGuid().ToString("N"));

    public ExecutableBitsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string File_(string rel)
    {
        var p = Path.Combine(_dir, Path.Combine(rel.Split('/')));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return p;
    }

    private string[] Names() => ExecutableBits.Candidates(_dir)
        .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void RenPy7_Layout_wird_erfasst()
    {
        // Der Regressionsfall: das Muster hiess "py*-linux-*" und traf
        // lib/linux-x86_64/ nicht. Gemessen an "Guilty Pleasure" — gepatcht
        // wurde 1 Datei (die .sh) statt 6, das Spiel blieb unstartbar.
        File_("GuiltyPleasure.sh");
        File_("lib/linux-x86_64/GuiltyPleasure");
        File_("lib/linux-x86_64/python");

        Assert.Equal(new[] { "GuiltyPleasure", "GuiltyPleasure.sh", "python" }, Names());
    }

    [Fact]
    public void RenPy8_Layout_wird_weiterhin_erfasst()
    {
        File_("Spiel.sh");
        File_("lib/py3-linux-x86_64/Spiel");
        File_("lib/py2-linux-x86_64/Spiel");

        Assert.Equal(new[] { "Spiel", "Spiel", "Spiel.sh" }, Names());
    }

    [Fact]
    public void Nicht_Linux_Plattformordner_bleiben_unberuehrt()
    {
        File_("lib/windows-i686/Spiel.exe");
        File_("lib/py3-windows-x86_64/Spiel.exe");
        File_("lib/mac-x86_64/Spiel");
        File_("lib/darwin-x86_64/Spiel");

        Assert.Empty(ExecutableBits.Candidates(_dir));
    }

    [Fact]
    public void Geteilte_Bibliotheken_zaehlen_nicht()
    {
        File_("lib/linux-x86_64/Spiel");
        File_("lib/linux-x86_64/libpython2.7.so.1.0");
        File_("lib/linux-x86_64/libSDL2-2.0.so.0");
        File_("lib/linux-x86_64/librenpython.so");

        Assert.Equal(new[] { "Spiel" }, Names());
    }

    [Fact]
    public void Unterordner_werden_nicht_rekursiv_eingesammelt()
    {
        // Ren'Py 7 legt unter lib/<plattform>/lib/python2.7/ einen kompletten
        // Bytecode-Baum ab. Ein rekursiver Lauf fasste auf der echten Platte
        // 83 statt 35 Dateien an — .pyo und .egg sind Daten, keine Programme.
        File_("lib/linux-x86_64/Spiel");
        File_("lib/linux-x86_64/lib/python2.7/os.pyo");
        File_("lib/linux-x86_64/lib/python2.7/renpy/__init__.pyo");
        File_("lib/linux-x86_64/eggs/rsa-3.1.4-py2.7.egg");

        Assert.Equal(new[] { "Spiel" }, Names());
    }

    [Fact]
    public void Ordner_ohne_lib_liefert_nur_die_Startskripte()
    {
        File_("Spiel.sh");
        Assert.Equal(new[] { "Spiel.sh" }, Names());
    }

    [Fact]
    public void Nicht_existierender_Ordner_liefert_leer_statt_Exception()
    {
        Assert.Empty(ExecutableBits.Candidates(Path.Combine(_dir, "gibtsnicht")));
        Assert.Equal(0, ExecutableBits.Apply(Path.Combine(_dir, "gibtsnicht")));
    }

    [Fact]
    public void Apply_setzt_die_Bits_und_zaehlt_nur_echte_Aenderungen()
    {
        if (!OperatingSystem.IsLinux()) return;   // SetUnixFileMode ist Linux-only

        var launcher = File_("lib/linux-x86_64/Spiel");
        File_("Spiel.sh");
        var lib = File_("lib/linux-x86_64/libpython2.7.so.1.0");

        Assert.Equal(2, ExecutableBits.Apply(_dir));
        Assert.True(File.GetUnixFileMode(launcher).HasFlag(UnixFileMode.UserExecute));
        // Kontrolle: die .so wurde nicht angefasst.
        Assert.False(File.GetUnixFileMode(lib).HasFlag(UnixFileMode.UserExecute));

        // Zweiter Lauf aendert nichts mehr — idempotent.
        Assert.Equal(0, ExecutableBits.Apply(_dir));
    }
}
