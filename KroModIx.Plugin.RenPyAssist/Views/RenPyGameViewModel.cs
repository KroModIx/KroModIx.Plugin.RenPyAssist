using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Per-Ren'Py-Spiel-Detail-VM. Ab v0.3.0 gibt der Host pro
/// erkanntem Container eine eigene Sidebar-Kachel — pro Kachel wird ein
/// dedizierter View gerendert mit Cover, Version, Thread-URL-Feld und
/// Row-Actions.</summary>
public sealed partial class RenPyGameViewModel : ObservableObject
{
    private readonly GamesRegistry _registry;
    private readonly F95zoneClient _f95;
    private readonly CoverCache _covers;
    private readonly RenPyWorker _worker;
    private readonly GameUpdateInstaller _installer;
    private readonly IHostServices _host;
    private readonly string _containerPath;

    private RenPyGame _game;

    [ObservableProperty] private Bitmap? _cover;
    [ObservableProperty] private string _threadUrlDraft = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    public RenPyGameViewModel(RenPyGame game, GamesRegistry registry, F95zoneClient f95,
        CoverCache covers, RenPyWorker worker, GameUpdateInstaller installer, IHostServices host)
    {
        _game = game;
        _registry = registry;
        _f95 = f95;
        _covers = covers;
        _worker = worker;
        _installer = installer;
        _host = host;
        _containerPath = game.ContainerPath;
        _threadUrlDraft = game.ThreadUrl ?? "";

        _registry.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshFromRegistry);
        _ = LoadCoverAsync();
    }

    public string DisplayName => _game.DisplayName;
    public string ContainerPath => _game.ContainerPath;
    public string SubPathText => string.IsNullOrEmpty(_game.ActiveSubPath)
        ? "(Legacy-Layout — Container ist das Spiel)" : _game.ActiveSubPath!;
    public string VersionText => _game.LocalVersion is null ? "lokal: (?)" : $"lokal: {_game.LocalVersion}";
    public string RemoteText => _game.LastRemoteVersion is null ? "remote: —" : $"remote: {_game.LastRemoteVersion}";
    public string LastCheckedText => _game.LastCheckedUtc is null
        ? "" : $"zuletzt geprüft: {_game.LastCheckedUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
    public bool HasUpdate => _game.HasUpdate;
    public string UpdateBadgeText => _game.LastRemoteVersion is null
        ? "↑ Update" : $"↑ {_game.LastRemoteVersion}";

    private void RefreshFromRegistry()
    {
        var latest = _registry.Find(_containerPath);
        if (latest is null) return;
        _game = latest;
        ThreadUrlDraft = _game.ThreadUrl ?? "";
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SubPathText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(RemoteText));
        OnPropertyChanged(nameof(LastCheckedText));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateBadgeText));
        _ = LoadCoverAsync();
    }

    private async Task LoadCoverAsync()
    {
        if (string.IsNullOrEmpty(_game.CoverUrl)) { Cover = null; return; }
        var path = await _covers.EnsureAsync(_game.CoverUrl!);
        if (path is not null && File.Exists(path))
        {
            // Host-Sidebar-Kachel-Cover propagieren (Contracts v1.9.3+).
            // Bei älteren Hosts default-Impl = no-op, kein Fehler.
            try { _host.TrySetManualGameCover(_containerPath, path); }
            catch (Exception ex) { _host.Logger.Debug(ex, "TrySetManualGameCover fehlgeschlagen"); }
        }
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
                _registry.Update(_game);
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
        if (!url.Contains("f95zone.", StringComparison.OrdinalIgnoreCase))
        {
            var ok = await _host.Dialogs.ConfirmAsync("URL ungewöhnlich",
                $"„{url}\" enthält kein f95zone — trotzdem übernehmen?");
            if (!ok) return;
        }
        _game.ThreadUrl = url;
        _registry.Update(_game);
        _host.Notifications.Notify($"Thread verknüpft: {_game.DisplayName}", NotificationLevel.Success);
        await CheckNowAsync();
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (string.IsNullOrEmpty(_game.ThreadUrl))
        {
            StatusText = "Kein Thread-URL — nichts zu prüfen.";
            return;
        }
        try
        {
            IsBusy = true;
            StatusText = "Prüfe Thread …";
            await _worker.CheckNowAsync();
            StatusText = "Check fertig.";
        }
        catch (Exception ex)
        {
            StatusText = "Check-Fehler: " + ex.Message;
        }
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
            $"Sub-Ordner in den neuen kopiert. Der alte Sub-Ordner bleibt liegen — " +
            $"manuell aufräumen wenn die neue Version läuft.\n\nFortfahren?");
        if (!ok) return;

        try
        {
            IsBusy = true;
            StatusText = "Entpacke …";
            var result = await _installer.InstallAsync(_game, zip);
            if (result.Success)
            {
                _host.Notifications.Notify(
                    $"Update installiert: {_game.DisplayName} → {result.NewSubPath}",
                    NotificationLevel.Success);
                StatusText = "Update fertig.";
                RefreshFromRegistry();
            }
            else
            {
                await _host.Dialogs.ShowMessageAsync("Install fehlgeschlagen",
                    result.Error ?? "Unbekannter Fehler.");
                StatusText = "Fehler: " + result.Error;
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
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Ren'Py-Launcher-Start fehlgeschlagen: {L}", launcher);
        }
    }

    private static string? FindLauncher(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        if (OperatingSystem.IsLinux())
            return Directory.EnumerateFiles(dir, "*.sh").FirstOrDefault();
        return Directory.EnumerateFiles(dir, "*.exe")
            .FirstOrDefault(f => !f.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase));
    }
}
