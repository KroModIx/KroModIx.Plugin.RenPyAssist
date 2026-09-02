using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Pro-Spiel Einstellungen-Tab v0.5+: kombiniert die Spiel-spezifischen
/// Actions (Thread-URL, Play, Update installieren, Ordner, Prüfen) mit den
/// plugin-globalen Settings (f95zone-Login, Downloads-Watch-Ordner, Poll-
/// Intervall) — alles was in v0.4 in der Detail-View war ist hier drin.</summary>
public sealed partial class GameSettingsViewModel : ObservableObject, IDisposable
{
    private readonly GamesRegistry _registry;
    private readonly RenPySettingsService _settings;
    private readonly F95zoneClient _f95;
    private readonly F95zoneSessionStore _sessionStore;
    private readonly RenPyWorker _worker;
    private readonly GameUpdateFlow _updateFlow;
    private readonly IHostServices _host;
    // Nach RenameFolder mutable — der Detail-View wird beim Kachel-Klick
    // ohnehin neu gebaut (via ManualGameRenamed-Event im Host), aber solange
    // dieses VM lebt zeigen die restlichen Actions (Play, Open Folder, Check)
    // auf den neuen Pfad.
    private string _containerPath;

    private RenPyGame _game;
    private readonly EventHandler _registryChanged;

    // Spiel-spezifisch
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenThreadCommand))]
    private string _threadUrlDraft = "";
    [ObservableProperty] private string _gameStatus = "";
    [ObservableProperty] private bool _isBusy;

    // Plugin-global
    [ObservableProperty] private string _downloadsDir = "";
    [ObservableProperty] private int _intervalMinutes = 60;
    [ObservableProperty] private string _f95Username = "";
    [ObservableProperty] private string _f95Password = "";
    [ObservableProperty] private string _globalStatus = "";
    [ObservableProperty] private string _loginStatus = "";
    [ObservableProperty] private bool _isLoggingIn;

    public string DisplayName => _game.DisplayName;
    public string ContainerPathText => string.Format(Strings.T("status.container_prefix"), _game.ContainerPath);
    public string LastCheckedText => _game.LastCheckedUtc is null
        ? "" : string.Format(Strings.T("status.last_checked_prefix"),
            _game.LastCheckedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));

    public GameSettingsViewModel(RenPyGame game, GamesRegistry registry,
        RenPySettingsService settings, F95zoneClient f95, F95zoneSessionStore sessionStore,
        RenPyWorker worker, GameUpdateFlow updateFlow, IHostServices host)
    {
        _game = game;
        _registry = registry;
        _settings = settings;
        _f95 = f95;
        _sessionStore = sessionStore;
        _worker = worker;
        _updateFlow = updateFlow;
        _host = host;
        _containerPath = game.ContainerPath;
        _threadUrlDraft = game.ThreadUrl ?? "";

        _downloadsDir = _settings.Current.DownloadsWatchDir ?? "";
        _intervalMinutes = _settings.Current.CheckIntervalMinutes;
        _f95Username = _settings.Current.F95Username;
        UpdateLoginStatus();

        // v0.17.2: siehe RenPyGameViewModel — Handler-Feld statt Lambda,
        // damit der Host beim Verwerfen des Tabs abmelden kann.
        _registryChanged = (_, _) => Dispatcher.UIThread.Post(RefreshFromRegistry);
        _registry.Changed += _registryChanged;
    }

    public void Dispose() => _registry.Changed -= _registryChanged;

    private void RefreshFromRegistry()
    {
        var latest = _registry.Find(_containerPath);
        if (latest is null) return;
        _game = latest;
        ThreadUrlDraft = _game.ThreadUrl ?? "";
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(LastCheckedText));
    }

    private void UpdateLoginStatus()
    {
        LoginStatus = _f95.IsAuthenticated
            ? string.Format(Strings.T("status.login_logged_in_as"),
                F95Username.Length > 0 ? F95Username : Strings.T("status.login_logged_in_unknown"))
            : Strings.T("status.login_not_logged_in");
    }

    // ---- Spiel-spezifische Actions ----

    /// <summary>v0.17: Thread direkt aus den Einstellungen oeffnen — ohne
    /// URL-Copy-Paste in den Browser. Nimmt bewusst den Draft-Text und nicht
    /// nur die gespeicherte URL, damit ein frisch eingefuegter Link sofort
    /// pruefbar ist ("ist das der richtige Thread?") bevor gespeichert wird.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenThread))]
    private void OpenThread()
    {
        var url = LooksLikeUrl(ThreadUrlDraft) ? ThreadUrlDraft.Trim() : _game.ThreadUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        _host.Logger.Info("Öffne f95zone-Thread: {Url}", url);
        _host.Shell.OpenExternalUrl(url!);
    }

    private bool CanOpenThread => LooksLikeUrl(ThreadUrlDraft)
        || LooksLikeUrl(_game.ThreadUrl);

    private static bool LooksLikeUrl(string? value)
    {
        var v = value?.Trim();
        return !string.IsNullOrEmpty(v)
            && (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async Task SaveThreadUrlAsync()
    {
        var url = ThreadUrlDraft?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            if (!string.IsNullOrEmpty(_game.ThreadUrl))
            {
                var ok = await _host.Dialogs.ConfirmAsync(Strings.T("dialog.remove_link_title"),
                    string.Format(Strings.T("dialog.remove_link_msg"), _game.DisplayName));
                if (!ok) return;
                _game.ThreadUrl = null;
                _game.LastRemoteVersion = null;
                _game.Description = null;
                _game.Genres.Clear();
                _game.DescriptionTranslations.Clear();
                _registry.Update(_game);
                GameStatus = Strings.T("status.link_removed");
            }
            return;
        }
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.invalid_url_title"),
                Strings.T("dialog.invalid_url_msg"));
            return;
        }
        _game.ThreadUrl = url;
        _registry.Update(_game);
        GameStatus = Strings.T("status.thread_saved");
        _host.Notifications.Notify(
            string.Format(Strings.T("notify.thread_linked"), _game.DisplayName),
            NotificationLevel.Success);
        await CheckNowAsync();
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (string.IsNullOrEmpty(_game.ThreadUrl))
        {
            GameStatus = Strings.T("status.no_thread_check");
            return;
        }
        try
        {
            IsBusy = true;
            GameStatus = Strings.T("status.checking_thread");
            await _worker.CheckNowAsync();
            GameStatus = Strings.T("status.check_done");
        }
        catch (Exception ex) { GameStatus = string.Format(Strings.T("status.check_error"), ex.Message); }
        finally { IsBusy = false; }
    }

    /// <summary>Gleicher Ablauf wie beim Update-Badge in der Uebersicht:
    /// erst im Downloads-Ordner suchen, sonst Datei-Auswahl (v0.20.0). Die
    /// Kette selbst liegt in <see cref="GameUpdateFlow"/>, damit sie nicht an
    /// zwei Stellen gepflegt werden muss.</summary>
    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        try
        {
            IsBusy = true;
            await _updateFlow.InstallUpdateAsync(_game, msg => GameStatus = msg);
            RefreshFromRegistry();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Update-Install fehlgeschlagen: {Game}", _game.DisplayName);
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.install_fail_title"), ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ChooseSidebarCropAsync()
    {
        // Cover-Quelle: bevorzugt Local-Cover im Container, sonst Registry.LocalCoverPath.
        var srcCandidates = new[]
        {
            GameLocalStore.CoverPath(_containerPath),
            _game.LocalCoverPath ?? "",
        };
        string? src = null;
        foreach (var c in srcCandidates)
            if (!string.IsNullOrEmpty(c) && File.Exists(c)) { src = c; break; }
        if (src is null)
        {
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.no_cover_title"),
                Strings.T("dialog.no_cover_msg"));
            return;
        }
        var outputPath = GameLocalStore.SidebarCoverPath(_containerPath);
        var dialog = new CoverCropDialog(src, outputPath, _host)
        {
            OnCropSaved = path =>
            {
                try
                {
                    _host.TrySetManualGameCover(_containerPath, path);
                    _host.Notifications.Notify(
                        Strings.T("notify.sidebar_crop_saved"),
                        NotificationLevel.Success);
                }
                catch (Exception ex) { _host.Logger.Warn(ex, "TrySetManualGameCover nach Crop fehlgeschlagen"); }
            },
        };
        var owner = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk
            ? desk.MainWindow : null;
        if (owner is not null) await dialog.ShowDialog(owner); else dialog.Show();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var path = string.IsNullOrEmpty(_game.ActiveSubPath)
            ? _game.ContainerPath
            : Path.Combine(_game.ContainerPath, _game.ActiveSubPath!);
        _host.Shell.OpenDirectory(path);
    }

    /// <summary>v0.10: benennt den Container-Ordner auf der Platte um.
    /// Der Sidebar-Kachel-Titel folgt dem Ordnernamen (Host bezieht ihn aus
    /// <c>ManualGameEntry.DisplayName</c>, den er beim initialen Bulk-Add aus
    /// dem Ordner-Basename gebildet hat — hier bleibt er unangetastet, aber
    /// der neue Ordner ist im Filesystem der Wahrheit).
    ///
    /// <para>Ablauf: Text-Prompt (neuer Basename) → Directory.Move →
    /// GamesRegistry.Rekey → GameLocalStore folgt automatisch (er wandert
    /// mit dem Ordner) → <c>IHostServices.TryRenameManualGame</c> re-keyed
    /// die Host-Sidebar-Kachel und triggert Detail-View-Rebuild.</para></summary>
    [RelayCommand]
    private async Task RenameFolderAsync()
    {
        var oldPath = _containerPath;
        var oldName = Path.GetFileName(oldPath);
        var parent = Path.GetDirectoryName(oldPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(oldPath))
        {
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.rename_title"),
                string.Format(Strings.T("dialog.rename_missing_msg"), oldPath));
            return;
        }

        var newName = await TextInputDialog.PromptAsync(
            title: Strings.T("dialog.rename_title"),
            message: string.Format(Strings.T("dialog.rename_prompt"), oldName),
            initialValue: oldName,
            acceptLabel: Strings.T("btn.rename"));
        if (string.IsNullOrWhiteSpace(newName)) return;
        // Basename-Validierung: keine Path-Separatoren, keine reservierten Zeichen.
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.rename_title"),
                Strings.T("dialog.rename_invalid_chars"));
            return;
        }
        if (string.Equals(newName, oldName, StringComparison.Ordinal)) return;

        var newPath = Path.Combine(parent, newName);
        if (Directory.Exists(newPath) || File.Exists(newPath))
        {
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.rename_title"),
                string.Format(Strings.T("dialog.rename_target_exists"), newPath));
            return;
        }

        try
        {
            IsBusy = true;
            GameStatus = Strings.T("status.renaming");
            // Directory.Move ist auf demselben Filesystem atomar (rename(2)),
            // über FS-Grenzen ein Copy+Delete — dann längere Laufzeit, aber
            // wir tolerieren das.
            Directory.Move(oldPath, newPath);

            // Registry re-keyen (In-Memory + JSON)
            _registry.Rekey(oldPath, newPath);
            _containerPath = newPath;
            _game = _registry.Find(newPath) ?? _game;

            // Host: Manual-Kachel re-keyen (nur bei Contracts v1.10.3+, sonst
            // no-op). Bei no-op: Kachel verwaist bis App-Neustart.
            var hostAccepted = _host.TryRenameManualGame(oldPath, newPath);

            _host.Notifications.Notify(
                hostAccepted
                    ? string.Format(Strings.T("notify.folder_renamed"), oldName, newName)
                    : Strings.T("notify.folder_renamed_pending"),
                NotificationLevel.Success);
            GameStatus = Strings.T("status.renamed");
            OnPropertyChanged(nameof(ContainerPathText));
            OnPropertyChanged(nameof(DisplayName));
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Rename fehlgeschlagen: {Old} → {New}", oldPath, newPath);
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.rename_title"),
                string.Format(Strings.T("dialog.rename_fail_msg"), ex.Message));
            GameStatus = Strings.T("status.rename_fail");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Play()
    {
        var dir = string.IsNullOrEmpty(_game.ActiveSubPath)
            ? _game.ContainerPath
            : Path.Combine(_game.ContainerPath, _game.ActiveSubPath!);
        var launcher = FindLauncher(dir);
        if (launcher is null)
        {
            _ = _host.Dialogs.ShowMessageAsync(Strings.T("dialog.no_launcher_title"),
                string.Format(Strings.T("dialog.no_launcher_msg"), dir));
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(launcher)
            {
                WorkingDirectory = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { _host.Logger.Warn(ex, "Launcher-Start fehlgeschlagen: {L}", launcher); }
    }

    private static string? FindLauncher(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        if (OperatingSystem.IsLinux())
            return Directory.EnumerateFiles(dir, "*.sh").FirstOrDefault();
        return Directory.EnumerateFiles(dir, "*.exe")
            .FirstOrDefault(f => !f.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Plugin-globale Actions ----

    [RelayCommand]
    private async Task PickDownloadsAsync()
    {
        var picked = await _host.Dialogs.PickFolderAsync(Strings.T("dialog.pick_downloads"));
        if (!string.IsNullOrEmpty(picked)) DownloadsDir = picked;
    }

    [RelayCommand]
    private void SaveGlobalSettings()
    {
        _settings.Save(new RenPySettings
        {
            GamesRoot = _settings.Current.GamesRoot,
            DownloadsWatchDir = string.IsNullOrWhiteSpace(DownloadsDir) ? null : DownloadsDir,
            CheckIntervalMinutes = Math.Max(15, IntervalMinutes),
            F95Username = F95Username ?? "",
        });
        GlobalStatus = string.Format(Strings.T("status.global_saved"), DateTime.Now.ToString("HH:mm:ss"));
        _host.Notifications.Notify(Strings.T("notify.settings_saved"), NotificationLevel.Success);
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(F95Username) || string.IsNullOrWhiteSpace(F95Password))
        {
            LoginStatus = Strings.T("status.login_missing_creds");
            return;
        }
        try
        {
            IsLoggingIn = true;
            LoginStatus = Strings.T("status.login_logging_in");
            var ok = await _f95.LoginAsync(F95Username, F95Password);
            if (ok)
            {
                _sessionStore.Save(_f95.ExportCookies());
                _settings.Save(new RenPySettings
                {
                    GamesRoot = _settings.Current.GamesRoot,
                    DownloadsWatchDir = string.IsNullOrWhiteSpace(DownloadsDir) ? null : DownloadsDir,
                    CheckIntervalMinutes = Math.Max(15, IntervalMinutes),
                    F95Username = F95Username,
                });
                F95Password = "";
                LoginStatus = string.Format(Strings.T("status.login_ok"), F95Username);
                _host.Notifications.Notify(Strings.T("notify.f95_login_ok"), NotificationLevel.Success);
            }
            else { LoginStatus = Strings.T("status.login_fail"); }
        }
        catch (F95zoneAuthException ex) { LoginStatus = string.Format(Strings.T("status.login_prefix_fail"), ex.Message); }
        catch (Exception ex)
        {
            LoginStatus = string.Format(Strings.T("status.login_error"), ex.Message);
            _host.Logger.Warn(ex, "f95zone-Login-Ausnahme");
        }
        finally { IsLoggingIn = false; }
    }

    [RelayCommand]
    private void Logout()
    {
        _sessionStore.Save("");
        LoginStatus = Strings.T("status.logout_done");
        _host.Notifications.Notify(Strings.T("notify.f95_cookies_cleared"), NotificationLevel.Info);
    }

    [RelayCommand]
    private void OpenF95() => _host.Shell.OpenExternalUrl("https://f95zone.to/");
}
