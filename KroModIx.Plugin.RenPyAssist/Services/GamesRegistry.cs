using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Persistente Liste registrierter Ren'Py-Spiele in
/// <c>games.json</c>. Sync-Merge: beim <see cref="Rescan"/> werden neue
/// Container aus dem Root-Ordner hinzugefügt und weggefallene entfernt —
/// existierende Einträge behalten ihre f95zone-Metadata (ThreadUrl,
/// LastRemoteVersion, CoverUrl).</summary>
public sealed class GamesRegistry
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly RenPyPaths _paths;
    private readonly object _lock = new();
    private List<RenPyGame> _games = new();

    public GamesRegistry(RenPyPaths paths)
    {
        _paths = paths;
        _games = Load() ?? new List<RenPyGame>();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<RenPyGame> Games
    {
        get { lock (_lock) return _games.ToList(); }
    }

    /// <summary>Scannt den Root nach Ren'Py-Spielen und merged in die
    /// Registry. Neue Container werden hinzugefügt, gelöschte entfernt,
    /// bestehende behalten f95zone-Metadata aber Version/ActiveSubPath
    /// werden aus dem Filesystem aktualisiert.</summary>
    public void Rescan(string root)
    {
        var scanned = RenPyGameDetector.Scan(root).ToDictionary(g => g.ContainerPath,
            StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            // Entfernen: was nicht mehr im Filesystem existiert
            _games.RemoveAll(g => !scanned.ContainsKey(g.ContainerPath));

            // Update oder Hinzufügen
            foreach (var kv in scanned)
            {
                var existing = _games.FirstOrDefault(g =>
                    string.Equals(g.ContainerPath, kv.Key, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    _games.Add(kv.Value);
                }
                else
                {
                    existing.ActiveSubPath = kv.Value.ActiveSubPath;
                    existing.LocalVersion = kv.Value.LocalVersion;
                    // Name-Auto-Update nur wenn User keinen Override gesetzt hat
                    existing.Name = kv.Value.Name;
                }
            }
        }
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        Log.Info("Rescan {Root}: {N} Spiele in Registry", root, _games.Count);
    }

    /// <summary>Findet den Registry-Eintrag zu einem Container-Pfad
    /// (case-insensitive). Null wenn nicht registriert.</summary>
    public RenPyGame? Find(string containerPath)
    {
        lock (_lock)
        {
            return _games.FirstOrDefault(g =>
                string.Equals(g.ContainerPath, containerPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Ab v0.3 (Host-Wizard-Multi-Tile): pro Container legt der Host
    /// eine eigene Sidebar-Kachel an. Diese Methode wird pro DetectedGame
    /// aufgerufen und legt den Registry-Eintrag an falls er noch nicht
    /// existiert. Bei existierendem Eintrag: ActiveSubPath/LocalVersion aus
    /// dem Filesystem aktualisieren, f95zone-Metadata bleibt.
    ///
    /// <para><b>v0.5-Storage-Priorität:</b> zuerst lokal im Container gucken
    /// (<see cref="GameLocalStore"/>) — die Datei wandert mit dem Ordner. Nur
    /// wenn keine da, den zentralen Cache-Eintrag prüfen. Bei Änderungen
    /// werden beide aktualisiert (Container = Wahrheit, Cache = Index).</para></summary>
    public RenPyGame EnsureFromContainer(string containerPath)
    {
        var detected = RenPyGameDetector.DetectOne(containerPath);
        lock (_lock)
        {
            var existing = _games.FirstOrDefault(g =>
                string.Equals(g.ContainerPath, containerPath, StringComparison.OrdinalIgnoreCase));

            // Container-Local-Store hat immer Vorrang (User könnte Config von
            // anderem PC mitgebracht haben).
            var local = GameLocalStore.Load(containerPath);
            if (local is not null)
            {
                if (existing is null) { _games.Add(local); existing = local; }
                else MergeInto(existing, local);
            }

            if (existing is null)
            {
                var newGame = detected ?? new RenPyGame
                {
                    Name = Path.GetFileName(containerPath),
                    ContainerPath = containerPath,
                };
                _games.Add(newGame);
                GameLocalStore.Save(newGame);
                Save();
                Changed?.Invoke(this, EventArgs.Empty);
                return newGame;
            }
            if (detected is not null)
            {
                existing.ActiveSubPath = detected.ActiveSubPath;
                existing.LocalVersion = detected.LocalVersion;
            }
            GameLocalStore.Save(existing);
            Save();
            return existing;
        }
    }

    /// <summary>Merge Container-Local-Fields in bestehenden Registry-Eintrag.
    /// Local hat Vorrang für f95zone-Metadata (User-editiert), Registry hat
    /// Vorrang für ActiveSubPath/LocalVersion (aus Filesystem).</summary>
    private static void MergeInto(RenPyGame target, RenPyGame local)
    {
        if (!string.IsNullOrEmpty(local.ThreadUrl)) target.ThreadUrl = local.ThreadUrl;
        if (!string.IsNullOrEmpty(local.LastRemoteVersion)) target.LastRemoteVersion = local.LastRemoteVersion;
        if (local.LastCheckedUtc is not null) target.LastCheckedUtc = local.LastCheckedUtc;
        if (!string.IsNullOrEmpty(local.CoverUrl)) target.CoverUrl = local.CoverUrl;
        if (!string.IsNullOrEmpty(local.DisplayNameOverride)) target.DisplayNameOverride = local.DisplayNameOverride;
        if (!string.IsNullOrEmpty(local.Description)) target.Description = local.Description;
        if (local.Genres.Count > 0) target.Genres = local.Genres;
        if (local.DescriptionTranslations.Count > 0) target.DescriptionTranslations = local.DescriptionTranslations;
        if (!string.IsNullOrEmpty(local.LocalCoverPath)) target.LocalCoverPath = local.LocalCoverPath;
    }

    public void Update(RenPyGame updated)
    {
        lock (_lock)
        {
            var idx = _games.FindIndex(g =>
                string.Equals(g.ContainerPath, updated.ContainerPath, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _games[idx] = updated;
            else _games.Add(updated);
        }
        GameLocalStore.Save(updated);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string containerPath)
    {
        lock (_lock)
        {
            _games.RemoveAll(g =>
                string.Equals(g.ContainerPath, containerPath, StringComparison.OrdinalIgnoreCase));
        }
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Anzahl Spiele mit verfügbarem Update — für den Sidebar-
    /// Kachel-Badge via IUpdateNotifier.</summary>
    public int PendingUpdatesCount
    {
        get { lock (_lock) return _games.Count(g => g.HasUpdate); }
    }

    private List<RenPyGame>? Load()
    {
        try
        {
            if (!File.Exists(_paths.GamesRegistryPath)) return null;
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(_paths.GamesRegistryPath));
            return envelope?.Games;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Registry-Load fehlgeschlagen");
            return null;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_paths.GamesRegistryDir);
            var envelope = new Envelope { Games = _games };
            var json = JsonSerializer.Serialize(envelope,
                new JsonSerializerOptions { WriteIndented = true });
            var tmp = _paths.GamesRegistryPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_paths.GamesRegistryPath)) File.Delete(_paths.GamesRegistryPath);
            File.Move(tmp, _paths.GamesRegistryPath);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Registry-Save fehlgeschlagen");
        }
    }

    private sealed class Envelope
    {
        [JsonPropertyName("games")]
        public List<RenPyGame> Games { get; set; } = new();
    }
}
