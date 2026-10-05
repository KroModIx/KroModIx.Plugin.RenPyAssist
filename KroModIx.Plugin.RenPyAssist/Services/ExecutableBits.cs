using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Setzt die Unix-Exec-Bits auf die Ren'Py-Runtime eines Build-Ordners.
///
/// <para><b>Warum das nötig ist:</b> Ren'Py-Spiele kommen meist als ZIP, unter
/// Windows gepackt. Das ZIP-Format transportiert keine Unix-Permissions — unter
/// Linux entpackt liegt die Runtime als <c>-rw-r--r--</c> da. Das
/// <c>.sh</c>-Startskript macht am Ende <c>exec "$LIB/$BASEFILE"</c> und
/// scheitert mit Exitcode 126 („Keine Berechtigung"). Sichtbar wird das
/// nirgends: beim Doppelklick im Dateimanager gibt es kein Terminal, und das
/// Skript selbst startet ja — nur das <c>exec</c> darin nicht.</para>
///
/// <para><b>v0.23.0, zwei Lücken geschlossen:</b></para>
/// <list type="number">
/// <item>Das Muster hiess <c>py*-linux-*</c> und traf damit nur das
///   Ren'Py-8-Layout (<c>lib/py3-linux-x86_64/</c>). Ren'Py 7 legt die Runtime
///   nach <c>lib/linux-x86_64/</c> — dort griff nichts. Real gemessen an
///   „Guilty Pleasure" (Ren'Py 7): der Lauf patchte <b>1</b> Datei statt 7,
///   naemlich nur die <c>.sh</c>; das Spiel blieb unstartbar.</item>
/// <item>Der Fix lief nur beim Start ueber KroModIx. Nach einem Update war der
///   neue Sub-Ordner wieder ohne Bits, und von Hand startete das Spiel
///   weiterhin nicht. Real: „The Wife Secret" wurde in 0.6 gepatcht, 0.7 kam
///   wieder ohne.</item>
/// </list>
///
/// <para><b>Zuschnitt:</b> nur Dateien <b>direkt</b> in
/// <c>lib/&lt;plattform&gt;/</c>, keine Unterordner. Darunter liegt bei
/// Ren'Py 7 ein kompletter <c>lib/python2.7/</c>-Baum mit <c>.pyo</c>-Bytecode
/// und <c>.egg</c>-Archiven — Daten, keine Programme. Ein rekursiver Lauf
/// haette hier 83 statt 35 Dateien angefasst. <c>.so</c>-Bibliotheken bleiben
/// ebenfalls aussen vor: die werden geladen, nicht ausgefuehrt.</para></summary>
public static class ExecutableBits
{
    private const UnixFileMode ExecBits = UnixFileMode.UserExecute
        | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    /// <summary>Die Dateien, die ein <c>+x</c> brauchen — ohne etwas zu ändern.
    /// Oeffentlich, damit sich der Zuschnitt testen laesst, ohne Bits zu
    /// setzen.</summary>
    public static IReadOnlyList<string> Candidates(string buildDir)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(buildDir) || !Directory.Exists(buildDir)) return result;
        try
        {
            // Die Startskripte im Build-Ordner selbst.
            result.AddRange(Directory.EnumerateFiles(buildDir, "*.sh"));

            var libDir = Path.Combine(buildDir, "lib");
            if (!Directory.Exists(libDir)) return result;

            foreach (var platformDir in Directory.EnumerateDirectories(libDir)
                         .Where(d => IsLinuxPlatformDir(Path.GetFileName(d))))
            {
                // Bewusst NICHT rekursiv — siehe Zuschnitt in der Klassen-Doku.
                result.AddRange(Directory.EnumerateFiles(platformDir)
                    .Where(f => !IsSharedLibrary(f)));
            }
        }
        catch (Exception)
        {
            // Unlesbares Verzeichnis ist fuer den Aufrufer dasselbe wie „nichts
            // zu tun" — ein Start darf daran nicht scheitern.
        }
        return result;
    }

    /// <summary>Setzt <c>+x</c> auf alles aus <see cref="Candidates"/>, das es
    /// noch nicht hat. Rückgabe: Anzahl tatsächlich geänderter Dateien (0 =
    /// war schon in Ordnung, oder kein Linux). Best-effort — Fehler pro Datei
    /// werden übersprungen, damit eine einzelne read-only Datei nicht den
    /// ganzen Start kippt.</summary>
    public static int Apply(string buildDir)
    {
        if (!OperatingSystem.IsLinux()) return 0;
        int patched = 0;
        foreach (var file in Candidates(buildDir))
        {
            try
            {
                var mode = File.GetUnixFileMode(file);
                if ((mode & UnixFileMode.UserExecute) != 0) continue;
                File.SetUnixFileMode(file, mode | ExecBits);
                patched++;
            }
            catch (Exception) { /* einzelne Datei uebersprungen */ }
        }
        return patched;
    }

    /// <summary>Ren'Py-Plattform-Ordner unterhalb von <c>lib/</c>:
    /// <c>linux-x86_64</c>, <c>linux-i686</c> (Ren'Py 7) und
    /// <c>py2-linux-*</c>, <c>py3-linux-*</c> (Ren'Py 8). Windows- und
    /// Mac-Ordner bleiben unberuehrt — dort waere ein Exec-Bit sinnlos.</summary>
    private static bool IsLinuxPlatformDir(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.StartsWith("linux-", StringComparison.OrdinalIgnoreCase)) return true;
        // py<N>-linux-<arch>
        var dash = name.IndexOf("-linux-", StringComparison.OrdinalIgnoreCase);
        return dash > 0 && name.StartsWith("py", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Geteilte Bibliotheken: <c>libfoo.so</c>, <c>libfoo.so.1</c>,
    /// <c>libfoo.so.1.0</c>. Die werden geladen, nicht ausgeführt.</summary>
    private static bool IsSharedLibrary(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".so.", StringComparison.OrdinalIgnoreCase);
    }
}
