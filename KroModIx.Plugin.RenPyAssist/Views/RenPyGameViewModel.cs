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
    private readonly GameUpdateFlow _updateFlow;
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
    [ObservableProperty] private bool _isInstallingUpdate;
    /// <summary>Zwischenstand waehrend des Update-Installs (entpacken,
    /// Datei-Auswahl, Fehler) — direkt unter dem Badge sichtbar.</summary>
    [ObservableProperty] private string _updateStatus = "";

    private readonly GameInstallService _installer;
    /// <summary>Startverzeichnis der Archiv-Auswahl — der Watch-Ordner aus den
    /// Plugin-Einstellungen. Null ist in Ordnung, dann oeffnet der Dialog dort,
    /// wo das Betriebssystem meint.</summary>
    private readonly string? _downloadsDir;
    private readonly GameUninstallService _uninstaller;

    /// <summary>Laeuft gerade ein Anlegen oder Loeschen? Blockt beide Knoepfe,
    /// damit ein zweiter Klick nicht mitten in ein rekursives Delete faellt.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewGameFromArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteGameCommand))]
    private bool _isBusyWithGame;

    /// <summary>Zwischenstand von Anlegen/Loeschen, neben den Knoepfen sichtbar.</summary>
    [ObservableProperty] private string _gameActionStatus = "";

    public ObservableCollection<string> Genres { get; } = new();

    public RenPyGameViewModel(RenPyGame game, GamesRegistry registry,
        CoverCache covers, AiTranslator translator, GameUpdateFlow updateFlow,
        IHostServices host, GameInstallService installer,
        GameUninstallService uninstaller, string? downloadsDir)
    {
        _installer = installer;
        _uninstaller = uninstaller;
        _downloadsDir = downloadsDir;
        _game = game;
        _registry = registry;
        _covers = covers;
        _translator = translator;
        _updateFlow = updateFlow;
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
    /// <summary>v0.20.0: der Update-Badge ist klickbar. Ein Klick sucht im
    /// Downloads-Ordner nach der passenden Update-Datei und installiert sie —
    /// ohne Treffer oeffnet sich die Datei-Auswahl. Vorher fuehrte der Weg
    /// zwingend ueber den Einstellungen-Tab und den Datei-Dialog, obwohl die
    /// frisch heruntergeladene ZIP fast immer im Downloads-Ordner liegt.</summary>
    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (IsInstallingUpdate) return;
        try
        {
            IsInstallingUpdate = true;
            await _updateFlow.InstallUpdateAsync(_game, msg => UpdateStatus = msg);
            RefreshFromRegistry();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Update-Install fehlgeschlagen: {Game}", _game.DisplayName);
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.install_fail_title"), ex.Message);
        }
        finally
        {
            IsInstallingUpdate = false;
            UpdateStatus = "";
        }
    }

    [RelayCommand]
    private void OpenThread()
    {
        var url = _game.ThreadUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        _host.Logger.Info("Öffne f95zone-Thread: {Url}", url);
        _host.Shell.OpenExternalUrl(url!);
    }

    private bool CanActOnGame() => !IsBusyWithGame;

    /// <summary>v0.26.0: Neues Spiel aus einem Archiv im Downloads-Ordner.
    ///
    /// <para>Der Weg vorher war: Ordnerstruktur von Hand anlegen, Archiv
    /// hineinkopieren, auspacken, den Host-Wizard suchen lassen, dann den
    /// Thread nachtragen. Hier ist es ein Dateidialog, ein Name und
    /// optional der Thread.</para></summary>
    [RelayCommand(CanExecute = nameof(CanActOnGame))]
    private async Task NewGameFromArchiveAsync()
    {
        if (IsBusyWithGame) return;

        // Der Spiele-Ordner wird aus den vorhandenen Containern abgeleitet —
        // es gibt keine Einstellung dafuer, die verlaesslich gefuellt waere.
        var root = GameInstallService.GuessGamesRoot(_registry.Games);
        if (root is null)
        {
            await _host.Dialogs.ShowMessageAsync(
                Strings.T("dialog.no_games_root"), Strings.T("dialog.no_games_root_msg"));
            return;
        }

        var archiv = await _host.Dialogs.PickFileInAsync(
            Strings.T("dialog.pick_archive"), _downloadsDir,
            ("Archiv", new[] { "*.zip", "*.rar", "*.7z" }));
        if (string.IsNullOrWhiteSpace(archiv)) return;

        if (!_installer.LooksLikeRenPyGame(archiv!))
        {
            await _host.Dialogs.ShowMessageAsync(
                Strings.T("dialog.not_renpy"), Strings.T("dialog.not_renpy_msg"));
            return;
        }

        // Name aus dem Build-Ordner im Archiv, sonst aus dem Dateinamen — der
        // Build-Ordner ist der genauere Ausgangspunkt.
        var quelle = _installer.DetectBuildFolder(archiv!) ?? archiv!;
        var vorschlag = ArchiveNameParser.SuggestName(quelle);

        var wunsch = await NewGameDialog.PromptAsync(archiv!, root, vorschlag);
        if (wunsch is null) return;

        try
        {
            IsBusyWithGame = true;
            GameActionStatus = string.Format(Strings.T("dialog.new_game_running"), wunsch.DisplayName);

            var res = await _installer.InstallAsync(wunsch.ArchivePath, root, wunsch.DisplayName);
            if (!res.Success || res.ContainerPath is null)
            {
                await _host.Dialogs.ShowMessageAsync(
                    Strings.T("dialog.new_game_fail"), res.Error ?? "");
                return;
            }

            // Registry zuerst: dann traegt die Kachel ab der ersten Sekunde
            // Version und Thread, statt sie beim naechsten Scan nachzuziehen.
            var eintrag = _registry.EnsureFromContainer(res.ContainerPath);
            if (!string.IsNullOrWhiteSpace(wunsch.ThreadUrl))
            {
                eintrag.ThreadUrl = wunsch.ThreadUrl;
                _registry.Update(eintrag);
            }

            // Erst jetzt beim Host anmelden. Der legt die Kachel an und meldet
            // das Spiel per OnGameAddedAsync zurueck — die Registry steht da
            // schon, der Rueckruf findet also einen vollstaendigen Eintrag.
            if (!_host.TryAddManualGame(wunsch.DisplayName, res.ContainerPath, "renpy"))
                _host.Logger.Warn("Kachel fuer {Name} konnte nicht angelegt werden "
                    + "(Host < v1.35 oder Pfad schon belegt)", wunsch.DisplayName);

            _host.Notifications.Notify(
                string.Format(Strings.T("dialog.new_game_done"), wunsch.DisplayName),
                NotificationLevel.Success);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Neues Spiel anlegen fehlgeschlagen");
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.new_game_fail"), ex.Message);
        }
        finally { IsBusyWithGame = false; GameActionStatus = ""; }
    }

    /// <summary>v0.26.0: Spiel loeschen — Platte und Kachel.
    ///
    /// <para>Die Rueckfrage nennt die Groesse, damit der User weiss, worueber
    /// er entscheidet. Reihenfolge: erst die Platte, dann abmelden — scheitert
    /// das Loeschen, bleibt die Kachel stehen statt eine verwaiste Luecke zu
    /// hinterlassen.</para></summary>
    [RelayCommand(CanExecute = nameof(CanActOnGame))]
    private async Task DeleteGameAsync()
    {
        if (IsBusyWithGame) return;
        var container = _game.ContainerPath;

        var veto = _uninstaller.Veto(container);
        if (veto is not null)
        {
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.delete_game_fail"), veto);
            return;
        }

        var groesse = await Task.Run(() => GameUninstallService.DirectorySize(container));
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.delete_game_title"),
            string.Format(Strings.T("dialog.delete_game_msg"),
                _game.DisplayName, GameUninstallService.FormatSize(groesse)),
            okLabel: Strings.T("dialog.delete_game_ok"),
            cancelLabel: Strings.T("btn.cancel"));
        if (!ok) return;

        try
        {
            IsBusyWithGame = true;
            var res = await _uninstaller.UninstallAsync(container);
            if (!res.Success)
            {
                await _host.Dialogs.ShowMessageAsync(
                    Strings.T("dialog.delete_game_fail"), res.Error ?? "");
                return;
            }
            // Platte ist weg, Registry auch — jetzt die Kachel.
            if (!_host.TryRemoveManualGame(container))
                _host.Logger.Warn("Kachel fuer {Dir} konnte nicht abgemeldet werden "
                    + "(Host < v1.35?) — sie verschwindet spaetestens beim naechsten Start", container);

            _host.Notifications.Notify(
                string.Format(Strings.T("dialog.delete_game_done"),
                    _game.DisplayName, GameUninstallService.FormatSize(res.BytesFreed)),
                NotificationLevel.Success);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Spiel loeschen fehlgeschlagen: {Dir}", container);
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.delete_game_fail"), ex.Message);
        }
        finally { IsBusyWithGame = false; GameActionStatus = ""; }
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
