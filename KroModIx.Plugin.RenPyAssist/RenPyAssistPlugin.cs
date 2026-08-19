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
public sealed class RenPyAssistPlugin : IGameModPlugin, IUpdateNotifier, IGameLauncher
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.renpyassist",
        DisplayName: "Ren'Py Assist",
        Version: "0.16.1",
        Author: "Kroste",
        Description: "Verwaltet Ren'Py-Spiele als eigenständige Sidebar-Kacheln " +
            "(Multi-Tile). v0.16.1: Cover-Crop-Dialog goldener Rahmen ist wieder " +
            "sichtbar — Race-Condition zwischen async Bitmap-Load und Canvas-" +
            "SizeChanged behoben (LayoutContent wird nach Bitmap-Decode explizit " +
            "nachgezogen wenn Canvas bereits Bounds hat). v0.16.0: KrosteTranslationGenerator schreibt Language-Activator " +
            "(krostemod_language_activator_<lang>.rpy im Game-Root) — aktiviert die Sprache " +
            "beim ersten Start + Overlay-Button unten rechts. Fixt Games mit hartcodiertem " +
            "Language-Cycle (SteamCity, viele Community-Renpy-Projekte), die den tl/<lang>/-" +
            "Ordner ignoriert haben weil kein UI-Button existierte. v0.15.0: Bitmap-Decode via " +
            "IHostServices.Images. v0.14.1: GIF-Cover-Guard. v0.14.0: DE+EN. " +
            "Setup via Host-Wizard '🎮 Ordner mit Spielen scannen' " +
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
    private RpycBatchService? _rpycBatch;
    private IReadOnlyList<DetectedGame> _activatedGames = Array.Empty<DetectedGame>();

    public Task InitializeAsync(IHostServices host, IReadOnlyList<DetectedGame> activatedGames, CancellationToken ct)
    {
        Strings.Init(host.Localization);
        _host = host;
        _paths = new RenPyPaths(host);
        _settings = new RenPySettingsService(_paths);
        _registry = new GamesRegistry(_paths);
        _f95 = new F95zoneClient();
        _sessionStore = new F95zoneSessionStore(_paths.F95zoneCookiesPath, host.Secrets);
        _covers = new CoverCache(_paths.CoverCacheDir, _f95);
        _worker = new RenPyWorker(_registry, _f95, _settings, _covers, host);
        _downloadWatcher = new DownloadWatcher();
        _installer = new GameUpdateInstaller(_registry);
        _rpaService = new RenpyArchiveService();
        _saveService = new RenpySaveService();
        _previewService = new MediaPreviewService();
        _translator = new AiTranslator(host);
        _modBuilder = new OneClickModBuilder();
        _rpycBatch = new RpycBatchService();
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
        var gifMigrationJobs = new List<(DetectedGame Game, string CoverUrl)>();
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
            // v0.8.4: GIF-Cover-Migration. Wenn die coverUrl auf .gif endet
            // und der Cache noch keinen v084-Marker hat, wurde das Bild mit
            // der alten Frame-0-Logik konvertiert (oft leer/blank). Container-
            // Mirror löschen und Cache-Warm im Hintergrund anwerfen — die
            // Kachel bleibt kurz bildlos statt ein blankes weisses Cover zu
            // zeigen. Nach Fetch: TrySetManualGameCover setzt die Kachel neu.
            var entry = _registry.Find(game.InstallDir);
            var mirrorPath = GameLocalStore.CoverPath(game.InstallDir);
            if (entry is not null
                && !string.IsNullOrEmpty(entry.CoverUrl)
                && _covers.NeedsV084GifMigration(entry.CoverUrl!)
                && File.Exists(mirrorPath))
            {
                try { File.Delete(mirrorPath); } catch { }
                gifMigrationJobs.Add((game, entry.CoverUrl!));
            }
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

        // v0.8.4: GIF-Migration-Jobs im Hintergrund (fire-and-forget) —
        // sonst blockiert der App-Start bei mehreren stalen GIF-Covern.
        // Cache-Warm → Container-Mirror → Sidebar-Update pro Job.
        if (gifMigrationJobs.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                foreach (var (game, url) in gifMigrationJobs)
                {
                    try
                    {
                        var cached = await _covers!.EnsureAsync(url, ct);
                        if (string.IsNullOrEmpty(cached)) continue;
                        var mirrored = GameLocalStore.CopyCoverIntoContainer(game.InstallDir, cached);
                        var effective = mirrored ?? cached;
                        host.TrySetManualGameCover(game.InstallDir, effective);
                    }
                    catch (Exception ex)
                    {
                        host.Logger.Debug(ex, "GIF-Migration fehlgeschlagen: {Dir}", game.InstallDir);
                    }
                }
                host.Logger.Info("GIF-Cover-Migration abgeschlossen: {N} Kacheln",
                    gifMigrationJobs.Count);
            }, ct);
        }

        var dlDir = _settings.Current.DownloadsWatchDir;
        if (!string.IsNullOrEmpty(dlDir))
        {
            _downloadWatcher.StableZipDetected += path =>
            {
                host.Notifications.Notify(
                    string.Format(Strings.T("notify.new_zip"), Path.GetFileName(path)),
                    NotificationLevel.Info);
            };
            _downloadWatcher.Start(dlDir);
        }

        _worker.Start();
        host.Logger.Info("Ren'Py Assist initialisiert: {N} Spiel-Kachel(n) registriert, " +
            "{C} Cover propagiert, {G} GIF-Migration(en) im Hintergrund, watchDir='{Dl}'",
            registered, coversPropagated, gifMigrationJobs.Count, dlDir);
        return Task.CompletedTask;
    }

    public IEnumerable<IGameTabContribution> GetTabContributions(DetectedGame game)
    {
        if (_host is null || _settings is null || _registry is null
            || _f95 is null || _sessionStore is null || _covers is null
            || _worker is null || _installer is null
            || _rpaService is null || _saveService is null || _previewService is null
            || _translator is null || _modBuilder is null || _rpycBatch is null)
            yield break;

        yield return new GameDetailTab(_registry, _covers, _translator, _host);
        yield return new ArchivesTab(_rpaService, _previewService, _registry, _host);
        yield return new SavesTab(_saveService, _registry, _host);
        yield return new ModsTab(_modBuilder, _rpycBatch, _registry, _host);
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
    /// Ab v0.8: nutzt <c>InstallDir</c> als Match-Key (Contracts v1.10.0+)
    /// — funktioniert für Engine-basierte Manual-Kacheln OHNE SteamAppId.</summary>
    public Task<IReadOnlyList<GameUpdateInfo>> GetPendingUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_registry is null || _activatedGames.Count == 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());

        var infos = new List<GameUpdateInfo>();
        foreach (var game in _activatedGames)
        {
            var entry = _registry.Find(game.InstallDir);
            if (entry is null || !entry.HasUpdate) continue;
            infos.Add(new GameUpdateInfo(
                SteamAppId: game.Target.SteamAppId ?? 0,
                PendingCount: 1,
                Summary: string.Format(Strings.T("notify.update_available_summary"), entry.LastRemoteVersion))
            {
                InstallDir = game.InstallDir,
            });
        }
        return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(infos);
    }

    // ---- IGameLauncher (v0.8+) ----

    /// <summary>Doppelklick auf die Sidebar-Kachel. Wenn ein Update verfügbar
    /// ist: öffnet den f95zone-Thread im Browser. Sonst: startet den Ren'Py-
    /// Launcher (<c>*.sh</c> auf Linux, <c>*.exe</c> auf Windows) im aktiven
    /// Sub-Ordner. Rückgabe true = Plugin hat's übernommen, false =
    /// Host-Default (was für Manual-Games ohne Executable ohnehin nicht viel
    /// bringen würde).</summary>
    public Task<bool> TryLaunchAsync(DetectedGame game, CancellationToken ct)
    {
        if (_registry is null || _host is null)
            return Task.FromResult(false);
        var entry = _registry.Find(game.InstallDir);
        if (entry is null) return Task.FromResult(false);

        // 1. Update vorhanden UND Thread-URL bekannt → Thread im Browser
        if (entry.HasUpdate && !string.IsNullOrWhiteSpace(entry.ThreadUrl))
        {
            _host.Shell.OpenExternalUrl(entry.ThreadUrl!);
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.update_thread_opened"),
                    entry.LastRemoteVersion, entry.DisplayName),
                NotificationLevel.Info);
            return Task.FromResult(true);
        }

        // 2. Ren'Py-Launcher-Search
        var dir = string.IsNullOrEmpty(entry.ActiveSubPath)
            ? entry.ContainerPath
            : System.IO.Path.Combine(entry.ContainerPath, entry.ActiveSubPath!);
        var launcher = FindRenpyLauncher(dir);
        if (launcher is null)
        {
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.no_launcher"), dir),
                NotificationLevel.Warning);
            return Task.FromResult(true); // wir sind zuständig, keine Host-Fallback-Chance
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launcher)
            {
                WorkingDirectory = dir,
                UseShellExecute = true,
            });
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.game_started"), entry.DisplayName),
                NotificationLevel.Success);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Ren'Py-Launcher-Start fehlgeschlagen: {L}", launcher);
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.game_start_fail"), ex.Message),
                NotificationLevel.Error);
        }
        return Task.FromResult(true);
    }

    private static string? FindRenpyLauncher(string dir)
    {
        if (!System.IO.Directory.Exists(dir)) return null;
        if (OperatingSystem.IsLinux())
            return System.IO.Directory.EnumerateFiles(dir, "*.sh").FirstOrDefault();
        return System.IO.Directory.EnumerateFiles(dir, "*.exe")
            .FirstOrDefault(f => !f.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase));
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
        public string Label => Strings.T("tab.overview");
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
        public string Label => Strings.T("tab.archives");
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
        public string Label => Strings.T("tab.saves");
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
        private readonly RpycBatchService _rpycBatch;
        private readonly GamesRegistry _registry;
        private readonly IHostServices _host;

        public ModsTab(OneClickModBuilder builder, RpycBatchService rpycBatch,
            GamesRegistry registry, IHostServices host)
        { _builder = builder; _rpycBatch = rpycBatch; _registry = registry; _host = host; }

        public string Id => "mods";
        public string Label => Strings.T("tab.mods");
        public string Icon => "\U0001F6E0"; // 🛠
        public int Order => 25;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host)
        {
            var entry = _registry.EnsureFromContainer(game.InstallDir);
            return new ModsView
            {
                DataContext = new ModsViewModel(entry.ContainerPath, entry.ActiveSubPath,
                    _builder, _rpycBatch, _host),
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
        public string Label => Strings.T("tab.settings");
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
