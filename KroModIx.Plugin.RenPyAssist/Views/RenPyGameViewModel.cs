using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Per-Ren'Py-Spiel-Übersichts-VM v0.5+ — reine Read-only-Ansicht.
/// Layout: Titel groß zentriert, Cover darunter zentriert, Beschreibung
/// (KI-übersetzt in System-Locale, Fallback Original), Genre-Chips.
/// Actions + Thread-URL sind in den Einstellungen-Tab gewandert.</summary>
public sealed partial class RenPyGameViewModel : ObservableObject
{
    private readonly GamesRegistry _registry;
    private readonly CoverCache _covers;
    private readonly AiTranslator _translator;
    private readonly IHostServices _host;
    private readonly string _containerPath;
    private RenPyGame _game;

    [ObservableProperty] private Bitmap? _cover;
    /// <summary>v0.11: Pfad zur Original-GIF-Datei (falls Cover animiert ist).
    /// View bindet auf GifImage.SourceUriRaw für autoplaying Loop. Bei null
    /// zeigt die View das statische Bitmap-Cover.</summary>
    [ObservableProperty] private string? _animatedCoverPath;
    public bool HasAnimatedCover => !string.IsNullOrEmpty(AnimatedCoverPath);
    partial void OnAnimatedCoverPathChanged(string? value) => OnPropertyChanged(nameof(HasAnimatedCover));
    [ObservableProperty] private string _descriptionText = "";
    [ObservableProperty] private bool _isTranslating;
    [ObservableProperty] private string _translationHint = "";

    public ObservableCollection<string> Genres { get; } = new();

    public RenPyGameViewModel(RenPyGame game, GamesRegistry registry,
        CoverCache covers, AiTranslator translator, IHostServices host)
    {
        _game = game;
        _registry = registry;
        _covers = covers;
        _translator = translator;
        _host = host;
        _containerPath = game.ContainerPath;

        _registry.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshFromRegistry);
        RefreshVisibleFields();
        _ = LoadCoverAsync();
        _ = LoadDescriptionAsync();
    }

    public string DisplayName => _game.DisplayName;
    public string SubPathText => string.IsNullOrEmpty(_game.ActiveSubPath)
        ? "" : $"Sub-Path: {_game.ActiveSubPath!}";
    public string VersionInfo
    {
        get
        {
            var local = _game.LocalVersion is null ? "?" : _game.LocalVersion;
            var remote = _game.LastRemoteVersion is null ? "—" : _game.LastRemoteVersion;
            return $"lokal: {local}  ·  remote: {remote}";
        }
    }
    public bool HasUpdate => _game.HasUpdate;
    public string UpdateBadgeText => _game.LastRemoteVersion is null
        ? "↑ Update" : $"↑ {_game.LastRemoteVersion}";
    public bool HasThread => !string.IsNullOrWhiteSpace(_game.ThreadUrl);
    public string NoThreadHint =>
        "Kein f95zone-Thread verknüpft. In den Einstellungen (⚙) rechts oben " +
        "kannst du den Thread-Link eintragen — dann erscheinen Cover, " +
        "Beschreibung, Genre und Update-Checks automatisch.";

    private void RefreshFromRegistry()
    {
        var latest = _registry.Find(_containerPath);
        if (latest is null) return;
        _game = latest;
        RefreshVisibleFields();
        _ = LoadCoverAsync();
        _ = LoadDescriptionAsync();
    }

    private void RefreshVisibleFields()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SubPathText));
        OnPropertyChanged(nameof(VersionInfo));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateBadgeText));
        OnPropertyChanged(nameof(HasThread));
        Genres.Clear();
        foreach (var g in _game.Genres) Genres.Add(g);
    }

    private async Task LoadCoverAsync()
    {
        if (string.IsNullOrEmpty(_game.CoverUrl))
        {
            // Fallback: Container-Local-Cover falls vorhanden (Ordner-Umzug)
            var local = GameLocalStore.CoverPath(_containerPath);
            if (File.Exists(local)) TrySetCoverFromFile(local);
            else Cover = null;
            return;
        }
        var path = await _covers.EnsureAsync(_game.CoverUrl!);
        if (path is not null && File.Exists(path))
        {
            // Spiegele Cover in Container (wandert mit dem Ordner)
            var mirrored = GameLocalStore.CopyCoverIntoContainer(_containerPath, path);
            if (mirrored is not null && !string.Equals(_game.LocalCoverPath, mirrored, StringComparison.Ordinal))
            {
                _game.LocalCoverPath = mirrored;
                _registry.Update(_game);
            }
            // Sidebar-Kachel: der User-gewählte Ausschnitt gewinnt gegen
            // das Auto-Cover. Wenn `.renpyassist/sidebar-cover.png` existiert,
            // den propagieren, sonst das Auto-Cover. Ohne diesen Check
            // überschreibt jeder View-Reload den gecropten Ausschnitt.
            var sidebarOverride = GameLocalStore.SidebarCoverPath(_containerPath);
            var effectiveSidebarPath = File.Exists(sidebarOverride) ? sidebarOverride : path;
            try { _host.TrySetManualGameCover(_containerPath, effectiveSidebarPath); }
            catch { }
        }
        // Detail-View zeigt immer das volle Cover (nicht den Sidebar-Ausschnitt)
        TrySetCoverFromFile(path);

        // v0.11: bei animiertem GIF-Cover den Original-Pfad an die View
        // durchreichen — GifImage rendert loop-animiert. Wenn kein GIF
        // (statisches Cover) → null, View zeigt Bitmap.
        // v0.12.2: wenn nur der Frame-konvertierte PNG im Cache liegt (nach
        // v0.8.4-Migration ohne Original-Persist), Original nachladen —
        // sonst bleibt die Detail-View statisch.
        var url = _game.CoverUrl ?? "";
        var animated = _covers.TryGetAnimatedOriginal(url);
        if (animated is null && url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            animated = await _covers.EnsureAnimatedOriginalAsync(url);
        var animatedFinal = animated;
        Dispatcher.UIThread.Post(() => AnimatedCoverPath = animatedFinal);
    }

    private void TrySetCoverFromFile(string? path)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (path is null || !File.Exists(path)) { Cover = null; return; }
                using var s = File.OpenRead(path);
                Cover = new Bitmap(s);
            }
            catch { Cover = null; }
        });
    }

    private async Task LoadDescriptionAsync()
    {
        var raw = _game.Description ?? "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            DescriptionText = string.IsNullOrWhiteSpace(_game.ThreadUrl)
                ? "" : "(Beschreibung wird beim nächsten Thread-Check geladen — 🔄 Prüfen in den Einstellungen klicken.)";
            TranslationHint = "";
            return;
        }
        var locale = AiTranslator.SystemLocale;
        if (locale == "en" || string.IsNullOrEmpty(locale))
        {
            DescriptionText = raw;
            TranslationHint = "";
            return;
        }
        // Sofort Original zeigen, dann im Hintergrund übersetzen
        if (_game.DescriptionTranslations.TryGetValue(locale, out var cached)
            && !string.IsNullOrEmpty(cached))
        {
            DescriptionText = cached;
            TranslationHint = $"(via KI übersetzt, Cache — Original s. Einstellungen)";
            return;
        }
        DescriptionText = raw;
        TranslationHint = "(Original — KI übersetzt gerade …)";
        try
        {
            IsTranslating = true;
            var translated = await _translator.TranslateAsync(_game);
            if (!string.Equals(translated, raw, StringComparison.Ordinal))
            {
                _registry.Update(_game); // Cache in Container schreiben
                DescriptionText = translated;
                TranslationHint = "(via KI übersetzt)";
            }
            else
            {
                TranslationHint = "(KI nicht verfügbar oder Übersetzung leer — Original angezeigt)";
            }
        }
        finally { IsTranslating = false; }
    }
}
