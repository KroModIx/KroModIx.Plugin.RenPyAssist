using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;
using KroModIx.Plugin.RenPyAssist.Views;

namespace KroModIx.Plugin.RenPyAssist;

/// <summary>Ren'Py Assist — Ordner-basierter Mod-Manager für Ren'Py-Spiele
/// mit f95zone.to-Anbindung. Kein Steam-App-Match — Aktivierung läuft
/// über einen Steam-Anchor (Proton Experimental, AppId 1493710) als
/// Placeholder, bis der Host einen ordner-basierten Discovery-Contract
/// bekommt (v0.2).</summary>
public sealed class RenPyAssistPlugin : IGameModPlugin, IUpdateNotifier
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.renpyassist",
        DisplayName: "Ren'Py Assist",
        Version: "0.2.0",
        Author: "Kroste",
        Description: "Ordner-basierter Mod-/Update-Manager für Ren'Py-Spiele. " +
            "F95zone-Anbindung mit CSRF-Login und Session-Cookie-Ablage " +
            "(verschlüsselt via Host-Secrets). Erkennt Sub-Path-Rotation " +
            "(mehrere Version-Sub-Ordner pro Container), zeigt Update-Badge " +
            "bei neuer f95zone-Version, installiert ZIP-Updates in einen " +
            "neuen Sub-Ordner und kopiert Save-Games automatisch.");

    // Anchor via Host-Wizard „🎮 Ordner mit Spielen scannen" (Host v1.8.0+):
    // der User wählt seinen Ren'Py-Root, der Host legt ein Manual-Game mit
    // SteamAppId 9000001 + InstallDir=<root> an. AppId 9000001 ist die
    // Kroste-Convention für Ren'Py-Sammel-Anchor — User sieht die Zahl nie.
    public IReadOnlyList<GameTarget> Targets { get; } = new[]
    {
        new GameTarget("renpy-anchor", "Ren'Py Games (Ordner-Sammlung)",
            SteamAppId: 9000001,
            AlternativeExecutableNames: Array.Empty<string>(),
            Platforms: Platforms.Both),
    };

    private IHostServices? _host;
    private RenPyPaths? _paths;
    private RenPySettingsService? _settings;
    private GamesRegistry? _registry;
    private F95zoneClient? _f95;
    private F95zoneSessionStore? _sessionStore;
    private CoverCache? _covers;
    private RenPyWorker? _worker;
    private DownloadWatcher? _downloadWatcher;
    private GameUpdateInstaller? _installer;

    public Task InitializeAsync(IHostServices host, IReadOnlyList<DetectedGame> activatedGames, CancellationToken ct)
    {
        _host = host;
        _paths = new RenPyPaths(host);
        _settings = new RenPySettingsService(_paths);
        _registry = new GamesRegistry(_paths);
        _f95 = new F95zoneClient();
        _sessionStore = new F95zoneSessionStore(_paths.F95zoneCookiesPath, host.Secrets);
        _covers = new CoverCache(_paths.CoverCacheDir, _f95);
        _worker = new RenPyWorker(_registry, _f95, _settings);
        _downloadWatcher = new DownloadWatcher();
        _installer = new GameUpdateInstaller(_registry);

        // Cookies aus verschlüsseltem Store restaurieren — falls User schon
        // eingeloggt war, kein Re-Login nötig.
        var cookieBlob = _sessionStore.Load();
        if (!string.IsNullOrEmpty(cookieBlob))
        {
            _f95.ImportCookies(cookieBlob);
            host.Logger.Info("f95zone-Cookies restauriert (authenticated={Auth})", _f95.IsAuthenticated);
        }

        // Root-Ermittlung (v0.2): DetectedGame.InstallDir vom Host-Wizard
        // hat Vorrang; nur wenn der leer/Placeholder ist, fällt es auf
        // Plugin-Settings.GamesRoot zurück (Backward-Compat für v0.1.x-
        // Setups die den Root manuell im Settings-Tab hatten).
        var wizardRoot = activatedGames
            .Select(g => g.InstallDir)
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)
                              && p != "/" && p != @"C:\"
                              && Directory.Exists(p));
        if (!string.IsNullOrWhiteSpace(wizardRoot)
            && !string.Equals(wizardRoot, _settings.Current.GamesRoot, StringComparison.Ordinal))
        {
            host.Logger.Info("Root vom Host-Wizard übernommen: {Root}", wizardRoot);
            var cur = _settings.Current;
            _settings.Save(new RenPySettings
            {
                GamesRoot = wizardRoot!,
                DownloadsWatchDir = cur.DownloadsWatchDir,
                CheckIntervalMinutes = cur.CheckIntervalMinutes,
                F95Username = cur.F95Username,
            });
        }

        // Initial-Rescan wenn Root gesetzt und existiert. Nicht blockierend —
        // im Hintergrund, damit die Plugin-Initialisierung schnell zurückkehrt.
        _ = Task.Run(() =>
        {
            try
            {
                var root = _settings.Current.GamesRoot;
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                {
                    _registry.Rescan(root);
                }
            }
            catch (Exception ex) { host.Logger.Warn(ex, "Initial-Rescan fehlgeschlagen"); }
        }, ct);

        // Download-Watcher starten (falls Ordner konfiguriert).
        var dlDir = _settings.Current.DownloadsWatchDir;
        if (!string.IsNullOrEmpty(dlDir))
        {
            _downloadWatcher.StableZipDetected += path =>
            {
                host.Notifications.Notify(
                    $"Neue Ren'Py-ZIP: {Path.GetFileName(path)}",
                    NotificationLevel.Info);
            };
            _downloadWatcher.Start(dlDir);
        }

        _worker.Start();
        host.Logger.Info("Ren'Py Assist initialisiert (root='{Root}', watchDir='{Dl}')",
            _settings.Current.GamesRoot, dlDir);
        return Task.CompletedTask;
    }

    public IEnumerable<IGameTabContribution> GetTabContributions(DetectedGame game)
    {
        if (_host is null || _settings is null || _registry is null
            || _f95 is null || _sessionStore is null || _covers is null
            || _worker is null || _installer is null)
            yield break;

        yield return new GamesTab(_registry, _settings, _f95, _covers, _worker, _installer, _host);
        yield return new SettingsTab(_settings, _f95, _sessionStore, _host);
    }

    public async Task ShutdownAsync()
    {
        if (_worker is not null) await _worker.DisposeAsync();
        _downloadWatcher?.Dispose();
        _f95?.Dispose();
        _host?.Logger.Info("Ren'Py Assist shutdown");
    }

    // ---- IUpdateNotifier ----

    /// <summary>Meldet dem Host wie viele Ren'Py-Spiele ein Update haben —
    /// der grüne ↑-Badge auf der Sidebar-Kachel (Proton-Experimental-Anchor)
    /// zeigt die Summe.</summary>
    public Task<IReadOnlyList<GameUpdateInfo>> GetPendingUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_registry is null || Targets[0].SteamAppId is null)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());

        var count = _registry.PendingUpdatesCount;
        if (count <= 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());

        var info = new GameUpdateInfo(
            SteamAppId: Targets[0].SteamAppId!.Value,
            PendingCount: count,
            Summary: $"{count} Ren'Py-Update(s) auf f95zone");
        return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(new[] { info });
    }

    // ---- Tab-Contributions ----

    private sealed class GamesTab : IGameTabContribution
    {
        private readonly GamesRegistry _registry;
        private readonly RenPySettingsService _settings;
        private readonly F95zoneClient _f95;
        private readonly CoverCache _covers;
        private readonly RenPyWorker _worker;
        private readonly GameUpdateInstaller _installer;
        private readonly IHostServices _host;

        public GamesTab(GamesRegistry registry, RenPySettingsService settings, F95zoneClient f95,
            CoverCache covers, RenPyWorker worker, GameUpdateInstaller installer, IHostServices host)
        {
            _registry = registry; _settings = settings; _f95 = f95;
            _covers = covers; _worker = worker; _installer = installer; _host = host;
        }

        public string Id => "games";
        public string Label => "Spiele";
        public string Icon => "\U0001F3AE"; // 🎮
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host)
            => new GamesView
            {
                DataContext = new GamesViewModel(_registry, _settings, _f95, _covers,
                    _worker, _installer, _host),
            };
    }

    private sealed class SettingsTab : IGameTabContribution
    {
        private readonly RenPySettingsService _settings;
        private readonly F95zoneClient _f95;
        private readonly F95zoneSessionStore _sessionStore;
        private readonly IHostServices _host;

        public SettingsTab(RenPySettingsService settings, F95zoneClient f95,
            F95zoneSessionStore sessionStore, IHostServices host)
        {
            _settings = settings; _f95 = f95; _sessionStore = sessionStore; _host = host;
        }

        public string Id => "settings";
        public string Label => "Einstellungen";
        public string Icon => "⚙";
        public int Order => 30;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host)
            => new SettingsView
            {
                DataContext = new SettingsViewModel(_settings, _f95, _sessionStore, _host),
            };
    }
}
