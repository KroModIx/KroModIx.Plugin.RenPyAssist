using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>ViewModel für den Einstellungen-Tab. Verwaltet Root-Ordner,
/// Downloads-Ordner, Poll-Intervall und f95zone-Login.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly RenPySettingsService _settings;
    private readonly F95zoneClient _f95;
    private readonly F95zoneSessionStore _sessionStore;
    private readonly IHostServices _host;

    [ObservableProperty] private string _gamesRoot = "";
    [ObservableProperty] private string _downloadsDir = "";
    [ObservableProperty] private int _intervalMinutes = 60;
    [ObservableProperty] private string _f95Username = "";
    [ObservableProperty] private string _f95Password = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _loginStatusText = "";
    [ObservableProperty] private bool _isLoggingIn;

    public SettingsViewModel(RenPySettingsService settings, F95zoneClient f95,
        F95zoneSessionStore sessionStore, IHostServices host)
    {
        _settings = settings;
        _f95 = f95;
        _sessionStore = sessionStore;
        _host = host;

        _gamesRoot = _settings.Current.GamesRoot;
        _downloadsDir = _settings.Current.DownloadsWatchDir ?? "";
        _intervalMinutes = _settings.Current.CheckIntervalMinutes;
        _f95Username = _settings.Current.F95Username;
        UpdateLoginStatus();
    }

    private void UpdateLoginStatus()
    {
        LoginStatusText = _f95.IsAuthenticated
            ? $"✔ Eingeloggt als {(F95Username.Length > 0 ? F95Username : "(unbekannt)")}"
            : "✘ Nicht eingeloggt";
    }

    [RelayCommand]
    private async Task PickRootAsync()
    {
        var picked = await _host.Dialogs.PickFolderAsync("Ren'Py-Root-Ordner wählen");
        if (!string.IsNullOrEmpty(picked)) GamesRoot = picked;
    }

    [RelayCommand]
    private async Task PickDownloadsAsync()
    {
        var picked = await _host.Dialogs.PickFolderAsync("Downloads-Ordner wählen");
        if (!string.IsNullOrEmpty(picked)) DownloadsDir = picked;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!string.IsNullOrWhiteSpace(GamesRoot) && !Directory.Exists(GamesRoot))
        {
            await _host.Dialogs.ShowMessageAsync("Root-Ordner ungültig",
                "Der Root-Ordner existiert nicht.");
            return;
        }
        _settings.Save(new RenPySettings
        {
            GamesRoot = GamesRoot ?? "",
            DownloadsWatchDir = string.IsNullOrWhiteSpace(DownloadsDir) ? null : DownloadsDir,
            CheckIntervalMinutes = Math.Max(15, IntervalMinutes),
            F95Username = F95Username ?? "",
        });
        StatusText = $"Gespeichert um {DateTime.Now:HH:mm:ss}.";
        _host.Notifications.Notify("Einstellungen gespeichert", NotificationLevel.Success);
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(F95Username) || string.IsNullOrWhiteSpace(F95Password))
        {
            LoginStatusText = "⚠ Bitte Username + Passwort eintragen.";
            return;
        }
        try
        {
            IsLoggingIn = true;
            LoginStatusText = "Logging in …";
            var ok = await _f95.LoginAsync(F95Username, F95Password);
            if (ok)
            {
                _sessionStore.Save(_f95.ExportCookies());
                // Username persistieren, Passwort NIE — Session-Cookie reicht.
                _settings.Save(new RenPySettings
                {
                    GamesRoot = GamesRoot,
                    DownloadsWatchDir = string.IsNullOrWhiteSpace(DownloadsDir) ? null : DownloadsDir,
                    CheckIntervalMinutes = Math.Max(15, IntervalMinutes),
                    F95Username = F95Username,
                });
                F95Password = ""; // aus Memory tilgen
                LoginStatusText = $"✔ Eingeloggt als {F95Username}";
                _host.Notifications.Notify("f95zone-Login erfolgreich", NotificationLevel.Success);
            }
            else
            {
                LoginStatusText = "✘ Login fehlgeschlagen (falsche Credentials?)";
            }
        }
        catch (F95zoneAuthException ex)
        {
            LoginStatusText = "✘ " + ex.Message;
        }
        catch (Exception ex)
        {
            LoginStatusText = "✘ Fehler: " + ex.Message;
            _host.Logger.Warn(ex, "f95zone-Login-Ausnahme");
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _sessionStore.Save("");
        LoginStatusText = "✘ Nicht eingeloggt (Cookies gelöscht — Plugin-Restart nötig um Session zu leeren)";
        _host.Notifications.Notify("f95zone-Cookies gelöscht", NotificationLevel.Info);
    }

    [RelayCommand]
    private void OpenF95()
        => _host.Shell.OpenExternalUrl("https://f95zone.to/");
}
