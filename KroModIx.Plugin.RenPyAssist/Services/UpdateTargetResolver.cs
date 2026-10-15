using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Wohin ein Update-Archiv ausgepackt wird — bestimmt aus dem
/// Archiv-Inhalt, <b>bevor</b> eine Datei geschrieben wird.</summary>
/// <param name="SubFolderName">Der Build-Unterordner im Container.</param>
/// <param name="ExtractInto">Das Verzeichnis, das <c>Extract</c> bekommt:
/// bei einem Archiv mit eigenem Build-Ordner der Container selbst, bei einem
/// flachen Archiv der Unterordner.</param>
/// <param name="IsInPlace">Der Unterordner existiert bereits — das Archiv
/// schreibt über die laufende Fassung. Dann müssen Spielstände vorher
/// gesichert werden, und hinterher gibt es keinen alten Ordner zum
/// Aufräumen.</param>
public sealed record UpdateTarget(string SubFolderName, string ExtractInto, bool IsInPlace);

/// <summary>Bestimmt das Auspack-Ziel eines Update-Archivs vorab.
///
/// <para><b>Warum das vorher passieren muss:</b> der Installer hat früher
/// ausgepackt und danach per Verzeichnis-Diff gesucht, welcher Unterordner
/// neu dazugekommen ist. Das scheitert, sobald das Archiv denselben
/// Ordnernamen trägt wie die installierte Fassung — und das kommt vor, weil
/// Entwickler den Build-Ordner zwischen Versionen nicht immer umbenennen.
/// Real aufgetreten am 15.10.2026: <c>The_Pleasures_of_Egypt_v0.3-windows.zip</c>
/// enthält den Ordner <c>THEPLEASURESOFEGYPT-0.2-win</c>, exakt den Namen
/// der installierten 0.2.</para>
///
/// <para>Die Folge war nicht nur eine falsche Meldung. Der Diff fand keinen
/// neuen Ordner und meldete „Nach dem Entpacken kein neuer Sub-Ordner mit
/// game/ gefunden" — <b>nachdem</b> alle 1430 Dateien bereits über die
/// laufende Installation geschrieben waren. Das Update war in Wahrheit
/// vollständig installiert, die Registry blieb auf der alten Version stehen,
/// und die Spielstände hätte dasselbe Schreiben treffen können: gesichert
/// wurden sie erst im Schritt nach dem Auspacken.</para></summary>
public static class UpdateTargetResolver
{
    /// <summary>Liest die Archiv-Einträge und entscheidet. Wirft nicht —
    /// kann das Archiv nicht gelesen werden, kommt null zurück und der
    /// Aufrufer bricht ab, bevor etwas geschrieben wurde.</summary>
    public static UpdateTarget? Resolve(IArchiveService archives, string archivePath,
        string containerPath, string? archiveFileNameFallback = null)
    {
        IReadOnlyList<ArchiveEntry> entries;
        try { entries = archives.List(archivePath); }
        catch (Exception) { return null; }
        if (entries.Count == 0) return null;

        // Ein Build-Ordner ist ein oberster Eintrag, unter dem „game/" liegt.
        var buildFolder = entries
            .Select(e => e.Path.Split('/'))
            .Where(p => p.Length >= 2
                        && string.Equals(p[1], "game", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[0])
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        if (buildFolder is not null)
        {
            var ziel = Path.Combine(containerPath, buildFolder);
            return new UpdateTarget(buildFolder, containerPath, Directory.Exists(ziel));
        }

        // Flaches Archiv: „game/" liegt ganz oben. Dann bauen wir den
        // Unterordner selbst, aus dem Archivnamen — so sieht die Struktur
        // hinterher gleich aus, egal wie gepackt wurde.
        bool flach = entries.Any(e =>
            string.Equals(e.Path.Split('/')[0], "game", StringComparison.OrdinalIgnoreCase));
        if (!flach) return null;

        var name = StripArchiveExtension(
            archiveFileNameFallback ?? Path.GetFileName(archivePath));
        if (string.IsNullOrWhiteSpace(name)) return null;
        var unterordner = Path.Combine(containerPath, name);
        return new UpdateTarget(name, unterordner, Directory.Exists(unterordner));
    }

    /// <summary>Schneidet nur bekannte Archiv-Endungen ab.
    /// <c>GetFileNameWithoutExtension</c> würde an einem Punkt im Namen
    /// hängenbleiben — „FT-Ch.8.98.95-pc.zip" verlöre sein „.95-pc".</summary>
    private static string StripArchiveExtension(string fileName)
    {
        foreach (var ext in new[] { ".zip", ".rar", ".7z" })
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return fileName[..^ext.Length];
        return fileName;
    }
}
