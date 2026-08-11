using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;
using KroModIx.Plugin.RenPyAssist.Services.Modding;
using KroModIx.Plugin.RenPyAssist.Services.Preview;
using KroModIx.Plugin.RenPyAssist.Services.Rpa;
using KroModIx.Plugin.RenPyAssist.Services.Saves;
using KroModIx.Plugin.RenPyAssist.Views;

namespace KroModIx.Plugin.RenPyAssist;

/// <summary>Ren'Py Assist v0.3 — Multi-Tile-Modell: jedes vom Host-Wizard
/// „🎮 Ordner mit Spielen scannen" erkannte Ren'Py-Spiel bekommt eine
/// eigene Sidebar-Kachel (Match via <c>Target.Engine = "renpy"</c>). Pro
/// Kachel rendert das Plugin einen dedizierten Detail-View (Cover, Version,
/// f95zone-Thread, Update-Actions).</summary>
public sealed class RenPyAssistPlugin : IGameModPlugin, IUpdateNotifier
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.renpyassist",
        DisplayName: "Ren'Py Assist",
        Version: "0.6.0",
        Author: "Kroste",
        Description: "Verwaltet Ren'Py-Spiele als eigenständige Sidebar-Kacheln " +
            "(Multi-Tile). Setup via Host-Wizard '🎮 Ordner mit Spielen scannen' " +
            "→ Host legt pro Ren'Py-Container eine Kachel mit engine=renpy an, " +
            "Plugin übernimmt. Sub-Path-Rotation für Updates, game/saves/ bleibt " +
            "erhalten. F95zone-Thread-Watch mit CSRF-Login und verschlüsselten " +
            "Session-Cookies.");

    // Engine-basiertes Match ab Host v1.9.0. Der Host matched jedes
    // Manual-Game mit Engine="renpy" gegen dieses Target — kein Steam-Bezug
    // nötig, keine harten SteamAppId-Konventionen mehr (9000001 war v0.1/0.2).
    public IReadOnlyList<GameTarget> Targets { get; } = new[]
    {
        new GameTarget("renpy-game", "Ren'Py-Spiel",
            SteamAppId: null,
            AlternativeExecutableNames: Array.Empty<string>(),
            Platforms: Platforms.Both)
        { Engine = "renpy" },
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
    // v0.4: RPA-Extract + Save-Editor + Media-Preview
    private RenpyArchiveService? _rpaService;
    private RenpySaveService? _saveService;
    private MediaPreviewService? _previewService;
    // v0.5: KI-Übersetzung (Beschreibung → System-Locale)
    private AiTranslator? _translator;
    // v0.6: Mod-Pipeline (KrosteMod aus RenPack — Walkthrough/Cheat/Rename)
    private OneClickModBuilder? _modBuilder;
    private IReadOnlyList<DetectedGame> _activatedGames = Array.Empty<DetectedGame>();

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
        _rpaService = new RenpyArchiveService();
        _saveService = new RenpySaveService();
        _previewService = new MediaPreviewService();
        _translator = new AiTranslator(host);
        _modBuilder = new OneClickModBuilder();
        _activatedGames = activatedGames;

        var cookieBlob = _sessionStore.Load();
        if (!string.IsNullOrEmpty(cookieBlob))
        {
            _f95.ImportCookies(cookieBlob);
            host.Logger.Info("f95zone-Cookies restauriert (authenticated={Auth})", _f95.IsAuthenticated);
        }

        // v0.3: pro DetectedGame (= eine Sidebar-Kachel) einen Registry-
        // Eintrag anlegen falls noch nicht da. RenPyGameDetector extrahiert
        // beim Anlegen ActiveSubPath + LocalVersion aus dem Filesystem.
        //
        // v0.5.3: nach Registry-Ensure pro Kachel auch das Cover an den
        // Host propagieren — sonst haben die Sidebar-Kacheln nach jedem
        // Neustart kein Bild bis der User die Detail-View öffnet. Priorität:
        // User-Crop (`.renpyassist/sidebar-cover.png`) > Container-Local-
        // Cover (`.renpyassist/cover.img`).
        int registered = 0, coversPropagated = 0;
        foreach (var game in activatedGames)
        {
            if (string.IsNullOrWhiteSpace(game.InstallDir)
                || !Directory.Exists(game.InstallDir))
            {
                host.Logger.Warn("Ren'Py-Kachel '{Name}' ignoriert — InstallDir fehlt: {Dir}",
                    game.Target.DisplayName, game.InstallDir);
                continue;
            }
            _registry.EnsureFromContainer(game.InstallDir);
            registered++;
            // Cover-Path an Host propagieren (falls schon lokal gespeichert).
            var sidebar = GameLocalStore.SidebarCoverPath(game.InstallDir);
            var full = GameLocalStore.CoverPath(game.InstallDir);
            var coverToUse = File.Exists(sidebar) ? sidebar
                           : File.Exists(full) ? full : null;
            if (coverToUse is not null)
            {
                try
                {
                    if (host.TrySetManualGameCover(game.InstallDir, coverToUse))
                        coversPropagated++;
                }
                catch (Exception ex) { host.Logger.Debug(ex, "Cover-Init-Propagate: {Dir}", game.InstallDir); }
            }
        }

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
        host.Logger.Info("Ren'Py Assist v0.5.3 initialisiert: {N} Spiel-Kachel(n) registriert, " +
            "{C} Cover propagiert, watchDir='{Dl}'", registered, coversPropagated, dlDir);
        return Task.CompletedTask;
    }

    public IEnumerable<IGameTabContribution> GetTabContributions(DetectedGame game)
    {
        if (_host is null || _settings is null || _registry is null
            || _f95 is null || _sessionStore is null || _covers is null
            || _worker is null || _installer is null
            || _rpaService is null || _saveService is null || _previewService is null
            || _translator is null || _modBuilder is null)
            yield break;

        yield return new GameDetailTab(_registry, _covers, _translator, _host);
        yield return new ArchivesTab(_rpaService, _previewService, _registry, _host);
        yield return new SavesTab(_saveService, _registry, _host);
        yield return new ModsTab(_modBuilder, _registry, _host);
        yield return new GameSettingsTab(_registry, _settings, _f95, _sessionStore,
            _worker, _installer, _host);
    }

    public async Task ShutdownAsync()
    {
        if (_worker is not null) await _worker.DisposeAsync();
        _downloadWatcher?.Dispose();
        _f95?.Dispose();
        _host?.Logger.Info("Ren'Py Assist shutdown");
    }

    // ---- IUpdateNotifier ----

    /// <summary>Meldet dem Host pro Ren'Py-Kachel individuell ob ein Update
    /// vorliegt — der grüne ↑-Badge erscheint dann pro Sidebar-Kachel.
    /// Braucht Steam-AppId; da wir engine-basiert matchen und keine echten
    /// AppIds haben, funktioniert der Badge in v0.3 nur wenn der User dem
    /// Manual-Game via Host-UI eine SteamAppId gibt — sonst kein Badge.</summary>
    public Task<IReadOnlyList<GameUpdateInfo>> GetPendingUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_registry is null || _activatedGames.Count == 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());

        var infos = new List<GameUpdateInfo>();
        foreach (var game in _activatedGames)
        {
            if (game.Target.SteamAppId is not int appId) continue;
            var entry = _registry.Find(game.InstallDir);
            if (entry is null || !entry.HasUpdate) continue;
            infos.Add(new GameUpdateInfo(
                SteamAppId: appId,
                PendingCount: 1,
                Summary: $"Ren'Py-Update verfügbar: {entry.LastRemoteVersion}"));
        }
        return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(infos);
    }

    // ---- Tab-Contributions ----

    private sealed class GameDetailTab : IGameTabContribution
    {
        private readonly GamesRegistry _registry;
        private readonly CoverCache _covers;
        private readonly AiTranslator _translator;
        private readonly IHostServices _host;

        public GameDetailTab(GamesRegistry registry, CoverCache covers,
            AiTranslator translator, IHostServices host)
        {
            _registry = registry; _covers = covers;
            _translator = translator; _host = host;
        }

        public string Id => "game";
        public string Label => "Übersicht";
        public string Icon => "\U0001F3AE"; // 🎮
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host)
        {
            var entry = _registry.EnsureFromContainer(game.InstallDir);
            return new RenPyGameView
            {
                DataContext = new RenPyGameViewModel(entry, _registry, _covers, _translator, _host),
            };
        }
    }

    private sealed class ArchivesTab : IGameTabContribution
    {
        private readonly RenpyArchiveService _rpa;
        private readonly MediaPreviewService _preview;
        private readonly GamesRegistry _registry;
        private readonly IHostServices _host;

        public ArchivesTab(RenpyArchiveService rpa, MediaPreviewService preview,
            GamesRegistry registry, IHostServices host)
        {
            _rpa = rpa; _preview = preview; _registry = registry; _host = host;
        }

        public string Id => "archives";
        public string Label => "Archive";
        public string Icon => "\U0001F4E6"; // 📦
        public int Order => 10;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host)
        {
            var entry = _registry.EnsureFromContainer(game.InstallDir);
            return new ArchivesView
            {
                DataContext = new ArchivesViewModel(entry.ContainerPath, entry.ActiveSubPath,
                    _rpa, _preview, _host),
            };
        }
    }

    private sealed class SavesTab : IGameTabContribution
    {
        private readonly RenpySaveService _saves;
        private readonly GamesRegistry _registry;
        private readonly IHostServices _host;

        public SavesTab(RenpySaveService saves, GamesRegistry registry, IHostServices host)
        { _saves = saves; _registry = registry; _host = host; }

        public string Id => "saves";
        public string Label => "Saves";
        public string Icon => "\U0001F4BE"; // 💾
        public int Order => 20;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host)
        {
            var entry = _registry.EnsureFromContainer(game.InstallDir);
            return new SavesView
            {
                DataContext = new SavesViewModel(entry.ContainerPath, entry.ActiveSubPath,
                    _saves, _host),
            };
        }
    }

    private sealed class ModsTab : IGameTabContribution
    {
        private readonly OneClickModBuilder _builder;
        private readonly GamesRegistry _registry;
        private readonly IHostServices _host;

        public ModsTab(OneClickModBuilder builder, GamesRegistry registry, IHostServices host)
        { _builder = builder; _registry = registry; _host = host; }

        public string Id => "mods";
        public string Label => "Mods";
        public string Icon => "\U0001F6E0"; // 🛠
        public int Order => 25;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host)
        {
            var entry = _registry.EnsureFromContainer(game.InstallDir);
            return new ModsView
            {
                DataContext = new ModsViewModel(entry.ContainerPath, entry.ActiveSubPath,
                    _builder, _host),
            };
        }
    }

    private sealed class GameSettingsTab : IGameTabContribution
    {
        private readonly GamesRegistry _registry;
        private readonly RenPySettingsService _settings;
        private readonly F95zoneClient _f95;
        private readonly F95zoneSessionStore _sessionStore;
        private readonly RenPyWorker _worker;
        private readonly GameUpdateInstaller _installer;
        private readonly IHostServices _host;

        public GameSettingsTab(GamesRegistry registry, RenPySettingsService settings,
            F95zoneClient f95, F95zoneSessionStore sessionStore,
            RenPyWorker worker, GameUpdateInstaller installer, IHostServices host)
        {
            _registry = registry; _settings = settings; _f95 = f95;
            _sessionStore = sessionStore; _worker = worker; _installer = installer;
            _host = host;
        }

        public string Id => "settings";
        public string Label => "Einstellungen";
        public string Icon => "⚙";
        public int Order => 30;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host)
        {
            var entry = _registry.EnsureFromContainer(game.InstallDir);
            return new GameSettingsView
            {
                DataContext = new GameSettingsViewModel(entry, _registry, _settings, _f95,
                    _sessionStore, _worker, _installer, _host),
            };
        }
    }
}
