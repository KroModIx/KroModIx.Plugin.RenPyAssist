using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Plugin-globale Einstellungen (v0.3+): Downloads-Watch, Poll-
/// Intervall, f95zone-Login. Der Ren'Py-Root wird nicht mehr hier gesetzt —
/// jedes Spiel ist eine eigene Sidebar-Kachel, deren Container-Pfad vom
/// Host-Wizard „🎮 Ordner mit Spielen scannen" kommt.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly RenPySettingsService _settings;
    private readonly F95zoneClient _f95;
    private readonly F95zoneSessionStore _sessionStore;
    private readonly IHostServices _host;

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
    private async Task PickDownloadsAsync()
    {
        var picked = await _host.Dialogs.PickFolderAsync("Downloads-Ordner wählen");
        if (!string.IsNullOrEmpty(picked)) DownloadsDir = picked;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _settings.Save(new RenPySettings
        {
            GamesRoot = _settings.Current.GamesRoot, // legacy, wird nicht mehr genutzt
            DownloadsWatchDir = string.IsNullOrWhiteSpace(DownloadsDir) ? null : DownloadsDir,
            CheckIntervalMinutes = Math.Max(15, IntervalMinutes),
            F95Username = F95Username ?? "",
        });
        StatusText = $"Gespeichert um {DateTime.Now:HH:mm:ss}.";
        _host.Notifications.Notify("Einstellungen gespeichert", NotificationLevel.Success);
        await Task.CompletedTask;
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
                _settings.Save(new RenPySettings
                {
                    GamesRoot = _settings.Current.GamesRoot,
                    DownloadsWatchDir = string.IsNullOrWhiteSpace(DownloadsDir) ? null : DownloadsDir,
                    CheckIntervalMinutes = Math.Max(15, IntervalMinutes),
                    F95Username = F95Username,
                });
                F95Password = "";
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
        finally { IsLoggingIn = false; }
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
