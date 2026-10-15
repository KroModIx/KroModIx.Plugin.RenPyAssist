using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Leitet aus einem Archiv- oder Build-Ordnernamen einen Vorschlag
/// für den Spielnamen ab.
///
/// <para><b>Ausdrücklich nur ein Vorschlag.</b> An den 14 Archiven, die hier
/// real neben den Spielen liegen, nachgemessen: der Container-Name ist in
/// mehreren Fällen gar nicht ableitbar.
/// <c>Guilty_Pleasures-Sophias_Chronicles-1.5-PC.zip</c> gehört zu „Guilty
/// Pleasures Sophias Forbidden Chronicles" — das Wort „Forbidden" steht
/// nirgends im Dateinamen. <c>Shadowsofseduction-0.5.08013-pc.zip</c> gehört
/// zu „Shadows of seduction", und wo die Wortgrenzen liegen, weiß nur ein
/// Mensch. Deshalb füllt der Vorschlag ein Eingabefeld vor, das der User
/// überschreiben kann, statt den Namen festzulegen.</para></summary>
public static class ArchiveNameParser
{
    /// <summary>Version im Dateinamen: <c>-0.8.1-</c>, <c>-v0.06-</c>,
    /// <c>-EP4.0b-</c>, <c>Ch.8.98.95</c>. Dieselbe Kernregel wie in
    /// <see cref="RenPyGameDetector"/>, hier aber zum Abschneiden.</summary>
    private static readonly Regex VersionPart = new(
        @"[-_\s.]v?\d+(?:\.\d+)+[a-z0-9]*(?=[-_\s.]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Plattform- und Vertriebs-Suffixe, die Ren'Py-Releases
    /// anhängen. Werden nur am Ende abgeschnitten, nie mitten im Namen —
    /// sonst verlöre „A Wife and Mother" sein „and".</summary>
    private static readonly string[] TrailingNoise =
    {
        "pc", "win", "windows", "linux", "mac", "osx", "all", "market",
        "patreon", "itchio", "itch", "public", "release", "final", "freebuild",
        "proof", "concept", "preview", "ea", "compressed", "unc", "uncensored",
    };

    /// <summary>Der Namensvorschlag zu einer Archiv-Datei oder einem
    /// Build-Ordner. Leerer String, wenn nichts Sinnvolles übrig bleibt —
    /// dann soll der Aufrufer den User fragen, nicht raten.</summary>
    public static string SuggestName(string archivePathOrFolderName)
    {
        if (string.IsNullOrWhiteSpace(archivePathOrFolderName)) return string.Empty;

        var stem = Path.GetFileName(archivePathOrFolderName.TrimEnd('/', '\\'));
        // Nur bekannte Archiv-Endungen abschneiden. Ein Punkt im Namen ist
        // haeufig („Ch.8.98.95"), GetFileNameWithoutExtension wuerde daran
        // haengenbleiben.
        foreach (var ext in new[] { ".zip", ".rar", ".7z" })
            if (stem.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            { stem = stem[..^ext.Length]; break; }

        // Version abschneiden — alles ab der ersten Versionsangabe gehoert
        // nicht mehr zum Namen.
        var m = VersionPart.Match(stem);
        if (m.Success && m.Index > 0) stem = stem[..m.Index];

        var words = Tokenize(stem);
        words = CutAtVersionMarker(words);
        words = TrimTrailingNoise(words);
        if (words.Count == 0) return string.Empty;

        return string.Join(' ', words);
    }

    /// <summary>Wörter, die eine Versionsangabe einleiten. Ab hier gehört
    /// nichts mehr zum Titel: <c>Furry_Tales-Ch.8.98.95</c>,
    /// <c>…Episode5.2</c>, <c>…Update28.v0.41.2</c>.
    ///
    /// <para><c>part</c> steht bewusst NICHT hier: „A Wifes Life Part2" und
    /// „Sophia Parker Hidden Desires Part 2" tragen es im Titel. Lieber ein
    /// Wort zu viel im Vorschlag als ein abgeschnittener Name.</para></summary>
    private static readonly string[] VersionMarkers =
    {
        "ch", "chapter", "ep", "episode", "update", "patch", "build", "rev",
        "version", "day", "season",
    };

    /// <summary>Schneidet ab dem ersten Versions-Marker ab — aber nur, wenn
    /// danach wirklich eine Zahl kommt oder das Markerwort selbst eine
    /// trägt. Ein Spiel namens „Chapter" bliebe damit stehen.</summary>
    private static List<string> CutAtVersionMarker(List<string> words)
    {
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i].ToLowerInvariant();
            var bare = w.TrimEnd("0123456789".ToCharArray());
            bool isMarker = VersionMarkers.Contains(bare) && bare.Length > 0;
            if (!isMarker) continue;
            bool zahlFolgt = bare.Length < w.Length
                             || (i + 1 < words.Count && words[i + 1].Any(char.IsDigit));
            if (!zahlFolgt) continue;
            if (i == 0) return words;   // nichts uebrig lassen waere schlimmer
            return words.Take(i).ToList();
        }
        return words;
    }

    /// <summary>Zerlegt in Wörter: an Trennzeichen, und wo keine sind, an
    /// CamelCase-Grenzen. <c>TheWifeSecret</c> → „The Wife Secret";
    /// <c>Furry_Tales</c> → „Furry Tales". Ein durchgehend klein
    /// geschriebener Name wie <c>Shadowsofseduction</c> hat keine Grenze,
    /// die sich erkennen liesse — der bleibt am Stueck.</summary>
    private static List<string> Tokenize(string stem)
    {
        var parts = stem.Split(new[] { '_', '-', ' ', '.', '+' },
            StringSplitOptions.RemoveEmptyEntries);
        var words = new List<string>();
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            if (part.Contains(' ')) { words.Add(part); continue; }
            words.AddRange(SplitCamelCase(part));
        }
        return words;
    }

    /// <summary>Trennt vor jedem Großbuchstaben, dem ein Kleinbuchstabe oder
    /// eine Ziffer vorausgeht. Abkürzungen in Versalien bleiben zusammen
    /// (<c>TI</c> in „Lisa-TI").</summary>
    private static IEnumerable<string> SplitCamelCase(string part)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < part.Length; i++)
        {
            if (i > 0 && char.IsUpper(part[i])
                && (char.IsLower(part[i - 1]) || char.IsDigit(part[i - 1])))
            {
                yield return sb.ToString();
                sb.Clear();
            }
            sb.Append(part[i]);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>Schneidet Plattform- und Vertriebswörter vom Ende ab, solange
    /// mindestens ein Wort übrig bleibt. „Linux" als erstes Wort eines Titels
    /// bliebe damit stehen — gewollt.</summary>
    private static List<string> TrimTrailingNoise(List<string> words)
    {
        while (words.Count > 1
               && (TrailingNoise.Contains(words[^1].ToLowerInvariant())
                   // „Furry Tales Ch" — die Zahl hinter dem Marker hat schon
                   // der Versions-Schnitt mitgenommen, das nackte Wort bleibt.
                   || VersionMarkers.Contains(words[^1].ToLowerInvariant())
                   || words[^1].All(char.IsDigit)))
            words.RemoveAt(words.Count - 1);
        return words;
    }
}
