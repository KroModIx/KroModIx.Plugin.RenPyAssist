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
public sealed partial class RenPyGameViewModel : ObservableObject, IDisposable
{
    private readonly GamesRegistry _registry;
    private readonly CoverCache _covers;
    private readonly AiTranslator _translator;
    private readonly IHostServices _host;
    private readonly string _containerPath;
    private RenPyGame _game;
    private readonly EventHandler _registryChanged;

    [ObservableProperty] private Bitmap? _cover;
    /// <summary>v0.11 / v0.12.3: IGifSource-Instanz für Avalonia.Labs.Gif
    /// GifImage. Source-Property erwartet IGifSource, ein simpler String-
    /// Pfad wird stumm ignoriert (kein TypeConverter in der Lib). Bei null
    /// zeigt die View das statische Bitmap-Cover.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnimatedCover))]
    private Avalonia.Labs.Gif.IGifSource? _animatedCoverSource;
    public bool HasAnimatedCover => AnimatedCoverSource is not null;
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

        // v0.17.2: Handler als Feld + Dispose. Als Lambda abonniert hielt das
        // Event diese VM fuer die restliche Session am Leben, nachdem der
        // Host-Tab-Cache sie verworfen hat — und jede Registry-Aenderung hat
        // in der Leiche Cover-Reload + KI-Uebersetzung neu angeworfen.
        _registryChanged = (_, _) => Dispatcher.UIThread.Post(RefreshFromRegistry);
        _registry.Changed += _registryChanged;
        RefreshVisibleFields();
        _ = LoadCoverAsync();
        _ = LoadDescriptionAsync();
    }

    public string DisplayName => _game.DisplayName;
    public string SubPathText => string.IsNullOrEmpty(_game.ActiveSubPath)
        ? "" : string.Format(Strings.T("status.subpath_prefix"), _game.ActiveSubPath!);
    public string VersionInfo
    {
        get
        {
            var local = _game.LocalVersion is null ? "?" : _game.LocalVersion;
            var remote = _game.LastRemoteVersion is null ? "—" : _game.LastRemoteVersion;
            return string.Format(Strings.T("status.version_line"), local, remote);
        }
    }
    public bool HasUpdate => _game.HasUpdate;
    public string UpdateBadgeText => _game.LastRemoteVersion is null
        ? Strings.T("status.update_badge")
        : string.Format(Strings.T("status.update_badge_with_version"), _game.LastRemoteVersion);
    public bool HasThread => !string.IsNullOrWhiteSpace(_game.ThreadUrl);
    /// <summary>Roh-URL für den Tooltip des Thread-Buttons — der User sieht
    /// beim Hovern wohin der Klick geht, ohne in die Einstellungen zu wechseln.</summary>
    public string ThreadUrl => _game.ThreadUrl ?? "";
    public string NoThreadHint => Strings.T("status.no_thread_hint");

    /// <summary>v0.17: Ein-Klick-Weg zum f95zone-Thread direkt aus der
    /// Übersicht. Vorher war der Thread nur über den Einstellungen-Tab
    /// (Copy-Paste der URL) oder den Update-Doppelklick auf der Sidebar-
    /// Kachel erreichbar — beides zu umständlich für den Normalfall
    /// „schnell im Thread nachlesen".</summary>
    [RelayCommand]
    private void OpenThread()
    {
        var url = _game.ThreadUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        _host.Logger.Info("Öffne f95zone-Thread: {Url}", url);
        _host.Shell.OpenExternalUrl(url!);
    }

    public void Dispose() => _registry.Changed -= _registryChanged;

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
        OnPropertyChanged(nameof(ThreadUrl));
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
            catch (Exception ex)
            {
                // Kein App-Fehler, aber der User sieht danach eine Kachel mit
                // altem/fehlendem Cover und hatte bisher keinerlei Spur davon.
                _host.Logger.Debug(ex, "Sidebar-Cover setzen fehlgeschlagen: {Path}", effectiveSidebarPath);
            }
        }
        // Detail-View zeigt immer das volle Cover (nicht den Sidebar-Ausschnitt)
        TrySetCoverFromFile(path);

        // v0.11: bei animiertem GIF-Cover die IGifSource-Instanz an die View
        // durchreichen — Avalonia.Labs.Gif GifImage rendert loop-animiert.
        // Wenn kein GIF (statisches Cover) → null, View zeigt Bitmap.
        // v0.12.2: wenn nur der Frame-konvertierte PNG im Cache liegt (nach
        // v0.8.4-Migration ohne Original-Persist), Original nachladen.
        // v0.12.3: GifImage.Source erwartet IGifSource, nicht String —
        // GifStreamSource.FromStream() wrappen. Off-UI weil der Ctor den
        // GifDecoder synchron aufmacht (bei 13 MB GIF spuerbar).
        var url = _game.CoverUrl ?? "";
        var animated = _covers.TryGetAnimatedOriginal(url);
        if (animated is null && url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            animated = await _covers.EnsureAnimatedOriginalAsync(url);
        Avalonia.Labs.Gif.IGifSource? gifSrc = null;
        if (animated is not null && File.Exists(animated))
        {
            try
            {
                gifSrc = await Task.Run(() =>
                    Avalonia.Labs.Gif.GifStreamSource.FromStream(File.OpenRead(animated)));
            }
            catch (Exception ex)
            {
                _host.Logger.Debug(ex, "GifStreamSource-Ctor fehlgeschlagen: {P}", animated);
            }
        }
        Dispatcher.UIThread.Post(() => AnimatedCoverSource = gifSrc);
    }

    private async void TrySetCoverFromFile(string? path)
    {
        try
        {
            if (path is null || !File.Exists(path))
            {
                await Dispatcher.UIThread.InvokeAsync(() => Cover = null);
                return;
            }
            // v0.15.0: zentraler Host-Baukasten IHostServices.Images
            // (Contracts v1.18+). GIF-Cover-Sonderfall bleibt UNBERUEHRT
            // (siehe AnimatedCoverSource oben — IGifSource-Pfad).
            var bmp = await _host.Images.DecodeFileAsync(path);
            await Dispatcher.UIThread.InvokeAsync(() => Cover = bmp);
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() => Cover = null);
        }
    }

    private async Task LoadDescriptionAsync()
    {
        var raw = _game.Description ?? "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            DescriptionText = string.IsNullOrWhiteSpace(_game.ThreadUrl)
                ? "" : Strings.T("status.desc_will_load");
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
            TranslationHint = Strings.T("status.desc_translated_cached");
            return;
        }
        DescriptionText = raw;
        TranslationHint = Strings.T("status.desc_translating");
        try
        {
            IsTranslating = true;
            var translated = await _translator.TranslateAsync(_game);
            if (!string.Equals(translated, raw, StringComparison.Ordinal))
            {
                _registry.Update(_game); // Cache in Container schreiben
                DescriptionText = translated;
                TranslationHint = Strings.T("status.desc_translated");
            }
            else
            {
                TranslationHint = Strings.T("status.desc_translation_unavailable");
            }
        }
        finally { IsTranslating = false; }
    }
}
