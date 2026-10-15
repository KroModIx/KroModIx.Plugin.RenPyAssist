using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Was beim Löschen eines Spiels passiert ist.</summary>
/// <param name="BytesFreed">Größe des gelöschten Containers, für die Meldung.</param>
public sealed record UninstallResult(bool Success, long BytesFreed, string? Error)
{
    public static UninstallResult Fail(string error) => new(false, 0, error);
}

/// <summary>Löscht ein Ren'Py-Spiel vollständig: Container von der Platte,
/// Eintrag aus der Plugin-Registry, Kachel aus dem Host.
///
/// <para><b>Reihenfolge ist Absicht.</b> Erst die Platte, dann abmelden.
/// Scheitert das Löschen, bleiben Registry und Kachel stehen — der User sieht
/// dann ein Spiel, das es noch gibt, statt eine verschwundene Kachel über
/// einem vollen Ordner. Andersherum wäre der Fehler unsichtbar und die Daten
/// wären verwaist.</para>
///
/// <para><b>Was geprüft wird, bevor etwas gelöscht wird:</b> der Container
/// muss der registrierte Pfad sein und darf nicht die Wurzel oder ein
/// Elternverzeichnis anderer Spiele sein. Ein rekursives Löschen ist die
/// destruktivste Operation im Plugin; sie bekommt dieselbe Sorgfalt wie der
/// Update-Pfad, der einmal Spielstände mitgenommen hat.</para></summary>
public sealed class GameUninstallService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly GamesRegistry _registry;

    public GameUninstallService(GamesRegistry registry) => _registry = registry;

    /// <summary>Prüft, ob dieser Container gelöscht werden darf. Rückgabe:
    /// null = in Ordnung, sonst der Grund für die Ablehnung.</summary>
    public string? Veto(string containerPath)
        => Veto(containerPath, _registry.Games.Select(g => g.ContainerPath));

    /// <summary>Die eigentliche Prüfung, von der Registry entkoppelt.
    ///
    /// <para>Öffentlich und statisch, damit sie ohne Host-Infrastruktur
    /// testbar ist: das ist der Code, der entscheidet, ob gleich ein
    /// rekursives Löschen läuft — er gehört vollständig unter Test, nicht nur
    /// über den Umweg eines gestubbten Plugin-Verzeichnisses.</para></summary>
    public static string? Veto(string containerPath, IEnumerable<string> bekannteContainer)
    {
        if (string.IsNullOrWhiteSpace(containerPath)) return "Kein Pfad angegeben.";
        string full;
        try { full = Path.GetFullPath(containerPath).TrimEnd(Path.DirectorySeparatorChar); }
        catch (Exception ex) { return $"Pfad nicht auflösbar: {ex.Message}"; }

        if (!Directory.Exists(full)) return "Der Ordner existiert nicht (mehr).";

        // Wurzel oder Laufwerk — darf nie passieren, waere katastrophal.
        var parent = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(parent)) return "Das ist ein Wurzelverzeichnis.";

        // Der Container muss in der Registry stehen. Damit kann ein Tippfehler
        // oder ein manipulierter Pfad nicht irgendeinen Ordner treffen.
        var alle = bekannteContainer
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => { try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar); }
                           catch { return null; } })
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
        var bekannt = alle.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        if (!bekannt) return "Dieser Ordner ist in Ren'Py Assist nicht registriert.";

        // Enthaelt der Container weitere registrierte Spiele, ist er in
        // Wahrheit der Spiele-Root — dann wuerde ein Loeschen alles mitnehmen.
        var enthaeltAndere = alle.Any(other =>
            !string.Equals(other, full, StringComparison.OrdinalIgnoreCase)
            && other.StartsWith(full + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
        if (enthaeltAndere)
            return "In diesem Ordner liegen weitere registrierte Spiele — das sieht nach dem "
                 + "Spiele-Hauptordner aus, nicht nach einem einzelnen Spiel.";

        return null;
    }

    /// <summary>Summe aller Dateigrößen im Container — für die Rückfrage
    /// („… und gibt 4,2 GB frei"), damit der User weiß, worüber er
    /// entscheidet.</summary>
    public static long DirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => { try { return f.Length; } catch { return 0L; } });
        }
        catch (Exception) { return 0; }
    }

    /// <summary>Löscht den Container und meldet das Spiel in der Registry ab.
    /// Das Abmelden beim Host macht der Aufrufer — der Dienst kennt
    /// <c>IHostServices</c> nicht.</summary>
    public async Task<UninstallResult> UninstallAsync(string containerPath, CancellationToken ct = default)
    {
        var veto = Veto(containerPath);
        if (veto is not null)
        {
            Log.Warn("Löschen abgelehnt für {Dir}: {Grund}", containerPath, veto);
            return UninstallResult.Fail(veto);
        }

        var size = await Task.Run(() => DirectorySize(containerPath), ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() => Directory.Delete(containerPath, recursive: true), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Container konnte nicht gelöscht werden: {Dir}", containerPath);
            return UninstallResult.Fail(ex.Message);
        }

        // Erst jetzt abmelden: scheitert oben etwas, bleibt das Spiel sichtbar.
        _registry.Remove(containerPath);
        Log.Info("Ren'Py-Spiel gelöscht: {Dir} ({MB:F0} MB frei)", containerPath, size / 1024.0 / 1024.0);
        return new UninstallResult(true, size, null);
    }

    /// <summary>„4,2 GB" statt „4509715660" — für Rückfrage und Meldung.</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.#} {units[u]}";
    }
}
