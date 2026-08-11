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
public sealed partial class GameSettingsViewModel : ObservableObject
{
    private readonly GamesRegistry _registry;
    private readonly RenPySettingsService _settings;
    private readonly F95zoneClient _f95;
    private readonly F95zoneSessionStore _sessionStore;
    private readonly RenPyWorker _worker;
    private readonly GameUpdateInstaller _installer;
    private readonly IHostServices _host;
    private readonly string _containerPath;

    private RenPyGame _game;

    // Spiel-spezifisch
    [ObservableProperty] private string _threadUrlDraft = "";
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
    public string ContainerPathText => $"Container: {_game.ContainerPath}";
    public string LastCheckedText => _game.LastCheckedUtc is null
        ? "" : $"zuletzt geprüft: {_game.LastCheckedUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}";

    public GameSettingsViewModel(RenPyGame game, GamesRegistry registry,
        RenPySettingsService settings, F95zoneClient f95, F95zoneSessionStore sessionStore,
        RenPyWorker worker, GameUpdateInstaller installer, IHostServices host)
    {
        _game = game;
        _registry = registry;
        _settings = settings;
        _f95 = f95;
        _sessionStore = sessionStore;
        _worker = worker;
        _installer = installer;
        _host = host;
        _containerPath = game.ContainerPath;
        _threadUrlDraft = game.ThreadUrl ?? "";

        _downloadsDir = _settings.Current.DownloadsWatchDir ?? "";
        _intervalMinutes = _settings.Current.CheckIntervalMinutes;
        _f95Username = _settings.Current.F95Username;
        UpdateLoginStatus();

        _registry.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshFromRegistry);
    }

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
            ? $"✔ Eingeloggt als {(F95Username.Length > 0 ? F95Username : "(unbekannt)")}"
            : "✘ Nicht eingeloggt";
    }

    // ---- Spiel-spezifische Actions ----

    [RelayCommand]
    private async Task SaveThreadUrlAsync()
    {
        var url = ThreadUrlDraft?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            if (!string.IsNullOrEmpty(_game.ThreadUrl))
            {
                var ok = await _host.Dialogs.ConfirmAsync("Verknüpfung entfernen",
                    $"Thread-URL für „{_game.DisplayName}\" entfernen?");
                if (!ok) return;
                _game.ThreadUrl = null;
                _game.LastRemoteVersion = null;
                _game.Description = null;
                _game.Genres.Clear();
                _game.DescriptionTranslations.Clear();
                _registry.Update(_game);
                GameStatus = "Verknüpfung entfernt.";
            }
            return;
        }
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await _host.Dialogs.ShowMessageAsync("URL ungültig",
                "Bitte eine vollständige http(s)://-URL eintragen.");
            return;
        }
        _game.ThreadUrl = url;
        _registry.Update(_game);
        GameStatus = "Thread gespeichert — prüfe jetzt …";
        _host.Notifications.Notify($"Thread verknüpft: {_game.DisplayName}", NotificationLevel.Success);
        await CheckNowAsync();
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (string.IsNullOrEmpty(_game.ThreadUrl))
        {
            GameStatus = "Kein Thread-URL — nichts zu prüfen.";
            return;
        }
        try
        {
            IsBusy = true;
            GameStatus = "Prüfe Thread …";
            await _worker.CheckNowAsync();
            GameStatus = "Check fertig.";
        }
        catch (Exception ex) { GameStatus = "Check-Fehler: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        var zip = await _host.Dialogs.PickFileAsync(
            $"Update-ZIP für „{_game.DisplayName}\"",
            ("Ren'Py-ZIPs", new[] { "*.zip" }));
        if (zip is null) return;
        var ok = await _host.Dialogs.ConfirmAsync("Update installieren",
            $"ZIP wird in „{_game.ContainerPath}\" entpackt. Save-Games werden aus dem alten " +
            $"Sub-Ordner in den neuen kopiert.\n\nFortfahren?");
        if (!ok) return;
        try
        {
            IsBusy = true;
            GameStatus = "Entpacke …";
            var result = await _installer.InstallAsync(_game, zip);
            if (result.Success)
            {
                _host.Notifications.Notify(
                    $"Update installiert: {_game.DisplayName} → {result.NewSubPath}",
                    NotificationLevel.Success);
                GameStatus = "Update fertig.";
                RefreshFromRegistry();
            }
            else
            {
                await _host.Dialogs.ShowMessageAsync("Install fehlgeschlagen",
                    result.Error ?? "Unbekannter Fehler.");
                GameStatus = "Fehler: " + result.Error;
            }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var path = string.IsNullOrEmpty(_game.ActiveSubPath)
            ? _game.ContainerPath
            : Path.Combine(_game.ContainerPath, _game.ActiveSubPath!);
        _host.Shell.OpenDirectory(path);
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
            _ = _host.Dialogs.ShowMessageAsync("Kein Launcher",
                $"Kein .sh/.exe im aktiven Sub-Ordner gefunden:\n{dir}");
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
        var picked = await _host.Dialogs.PickFolderAsync("Downloads-Ordner wählen");
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
        GlobalStatus = $"Gespeichert um {DateTime.Now:HH:mm:ss}.";
        _host.Notifications.Notify("Einstellungen gespeichert", NotificationLevel.Success);
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(F95Username) || string.IsNullOrWhiteSpace(F95Password))
        {
            LoginStatus = "⚠ Bitte Username + Passwort eintragen.";
            return;
        }
        try
        {
            IsLoggingIn = true;
            LoginStatus = "Logging in …";
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
                LoginStatus = $"✔ Eingeloggt als {F95Username}";
                _host.Notifications.Notify("f95zone-Login erfolgreich", NotificationLevel.Success);
            }
            else { LoginStatus = "✘ Login fehlgeschlagen (falsche Credentials?)"; }
        }
        catch (F95zoneAuthException ex) { LoginStatus = "✘ " + ex.Message; }
        catch (Exception ex)
        {
            LoginStatus = "✘ Fehler: " + ex.Message;
            _host.Logger.Warn(ex, "f95zone-Login-Ausnahme");
        }
        finally { IsLoggingIn = false; }
    }

    [RelayCommand]
    private void Logout()
    {
        _sessionStore.Save("");
        LoginStatus = "✘ Nicht eingeloggt (Cookies gelöscht — Plugin-Restart nötig)";
        _host.Notifications.Notify("f95zone-Cookies gelöscht", NotificationLevel.Info);
    }

    [RelayCommand]
    private void OpenF95() => _host.Shell.OpenExternalUrl("https://f95zone.to/");
}
