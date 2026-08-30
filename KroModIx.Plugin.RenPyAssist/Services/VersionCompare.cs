using System;
using System.Collections.Generic;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Ren'Py-Aufsatz auf den Contracts-Baukasten
/// <see cref="KroModIx.Plugin.Contracts.VersionCompare"/> (Host v1.27+).
///
/// <para>Die Parse- und Vergleichsregeln (0.10 &gt; 0.9, 1.0 == 1.0.0,
/// Hotfix-Buchstaben, Pre-Release-Woerter) leben jetzt zentral im Host —
/// vorher hatte jedes Plugin seine eigene Variante. Hier bleibt nur die
/// f95zone-spezifische Policy fuer den Fall, dass eine Seite gar keinen
/// numerischen Kern hat.</para>
///
/// <para><b>Warum ein eigener Fallback:</b> f95zone-Thread-Titel tragen
/// Versionen wie <c>Ep. 5</c>, <c>Final</c> oder <c>Chapter 12</c>. Der
/// Contracts-Baukasten meldet dafuer bewusst „nicht vergleichbar" statt zu
/// raten. Fuer Ren'Py ist die nuetzlichere Antwort: unterschiedliche
/// Schreibweise = melden. Lieber einmal zu viel als ein echtes Update
/// verschlucken — der User sieht die Versionen im Badge und entscheidet
/// selbst.</para></summary>
public static class VersionCompare
{
    /// <summary>Ist <paramref name="remote"/> echt neuer als
    /// <paramref name="local"/>? Bei gleich oder aelter: false.</summary>
    public static bool IsRemoteNewer(string? remote, string? local)
    {
        if (string.IsNullOrWhiteSpace(remote) || string.IsNullOrWhiteSpace(local))
            return false;
        if (Contracts.VersionCompare.Compare(remote, local) is int cmp) return cmp > 0;
        // Nicht vergleichbar → f95zone-Fallback (siehe Klassen-Doku).
        return !string.Equals(Normalize(remote), Normalize(local), StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc cref="KroModIx.Plugin.Contracts.VersionCompare.Compare"/>
    public static int? Compare(string? a, string? b) => Contracts.VersionCompare.Compare(a, b);

    /// <inheritdoc cref="KroModIx.Plugin.Contracts.VersionCompare.Comparer"/>
    public static IComparer<string?> Comparer => Contracts.VersionCompare.Comparer;

    /// <inheritdoc cref="KroModIx.Plugin.Contracts.VersionCompare.Normalize"/>
    public static string Normalize(string? version) => Contracts.VersionCompare.Normalize(version);
}
