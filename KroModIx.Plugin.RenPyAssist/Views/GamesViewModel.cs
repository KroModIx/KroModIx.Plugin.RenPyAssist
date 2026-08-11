using System;
using System.Collections.ObjectModel;
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

/// <summary>ViewModel für den Games-Tab. Zeigt Registry-Einträge als Cards,
/// bietet Rescan/Update-Check/Install-Update/Play/Remove/Set-Thread-URL.</summary>
public sealed partial class GamesViewModel : ObservableObject
{
    private readonly GamesRegistry _registry;
    private readonly RenPySettingsService _settings;
    private readonly F95zoneClient _f95;
    private readonly CoverCache _covers;
    private readonly RenPyWorker _worker;
    private readonly GameUpdateInstaller _installer;
    private readonly IHostServices _host;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private GameRow? _selected;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<GameRow> Rows { get; } = new();

    public GamesViewModel(GamesRegistry registry, RenPySettingsService settings,
        F95zoneClient f95, CoverCache covers, RenPyWorker worker,
        GameUpdateInstaller installer, IHostServices host)
    {
        _registry = registry;
        _settings = settings;
        _f95 = f95;
        _covers = covers;
        _worker = worker;
        _installer = installer;
        _host = host;

        _registry.Changed += (_, _) => Dispatcher.UIThread.Post(RebuildRows);
        RebuildRows();
    }

    partial void OnSearchTextChanged(string value) => RebuildRows();

    private void RebuildRows()
    {
        var filter = SearchText?.Trim() ?? string.Empty;
        var wanted = _registry.Games
            .Where(g => filter.Length == 0
                    || g.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.HasUpdate)
            .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Rows.Clear();
        foreach (var g in wanted)
        {
            var row = new GameRow(g);
            Rows.Add(row);
            _ = LoadCoverAsync(row);
        }
        StatusText = $"{Rows.Count} Spiel(e){(wanted.Any(g => g.HasUpdate) ? $" — {wanted.Count(g => g.HasUpdate)} Update(s)" : "")}";
    }

    private async Task LoadCoverAsync(GameRow row)
    {
        if (string.IsNullOrEmpty(row.Game.CoverUrl)) return;
        var path = await _covers.EnsureAsync(row.Game.CoverUrl!);
        Dispatcher.UIThread.Post(() => row.SetCoverFromPath(path));
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        var root = _settings.Current.GamesRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            await _host.Dialogs.ShowMessageAsync("Root fehlt",
                "Setze zuerst den Ren'Py-Root-Ordner im Einstellungen-Tab.");
            return;
        }
        try
        {
            IsBusy = true;
            await Task.Run(() => _registry.Rescan(root));
            _host.Notifications.Notify($"Rescan: {_registry.Games.Count} Spiele", NotificationLevel.Success);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "Prüfe Threads …";
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
    private async Task SetThreadUrlAsync(GameRow? row)
    {
        if (row is null) return;
        var url = row.ThreadUrlDraft?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            // Leer + bereits gesetzt = Verknüpfung entfernen.
            if (!string.IsNullOrEmpty(row.Game.ThreadUrl))
            {
                var ok = await _host.Dialogs.ConfirmAsync("Verknüpfung entfernen",
                    $"Thread-URL für „{row.DisplayName}\" entfernen?");
                if (!ok) return;
                row.Game.ThreadUrl = null;
                row.Game.LastRemoteVersion = null;
                _registry.Update(row.Game);
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
        row.Game.ThreadUrl = url;
        _registry.Update(row.Game);
        _host.Notifications.Notify($"Thread verknüpft: {row.DisplayName}", NotificationLevel.Success);
        await _worker.CheckNowAsync();
    }

    [RelayCommand]
    private async Task InstallUpdateAsync(GameRow? row)
    {
        if (row is null) return;
        var zip = await _host.Dialogs.PickFileAsync(
            $"Update-ZIP für „{row.DisplayName}\"",
            ("Ren'Py-ZIPs", new[] { "*.zip" }));
        if (zip is null) return;

        var ok = await _host.Dialogs.ConfirmAsync("Update installieren",
            $"ZIP wird in „{row.ContainerPath}\" entpackt. Save-Games werden aus dem alten " +
            $"Sub-Ordner in den neuen kopiert. Der alte Sub-Ordner bleibt liegen — " +
            $"manuell aufräumen wenn die neue Version läuft.\n\nFortfahren?");
        if (!ok) return;

        try
        {
            IsBusy = true;
            StatusText = "Entpacke …";
            var result = await _installer.InstallAsync(row.Game, zip);
            if (result.Success)
            {
                _host.Notifications.Notify(
                    $"Update installiert: {row.DisplayName} → {result.NewSubPath}",
                    NotificationLevel.Success);
                StatusText = "Update fertig.";
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
    private void OpenFolder(GameRow? row)
    {
        if (row is null) return;
        var path = string.IsNullOrEmpty(row.Game.ActiveSubPath)
            ? row.Game.ContainerPath
            : Path.Combine(row.Game.ContainerPath, row.Game.ActiveSubPath!);
        _host.Shell.OpenDirectory(path);
    }

    [RelayCommand]
    private void Play(GameRow? row)
    {
        if (row is null) return;
        var dir = string.IsNullOrEmpty(row.Game.ActiveSubPath)
            ? row.Game.ContainerPath
            : Path.Combine(row.Game.ContainerPath, row.Game.ActiveSubPath!);

        // Ren'Py-Launcher: <dir>/<name>.sh (Linux), <dir>/<name>.exe (Windows).
        // Wir suchen einfach die erste .sh (Linux) oder .exe im Verzeichnis.
        var launcher = FindLauncher(dir);
        if (launcher is null)
        {
            _host.Dialogs.ShowMessageAsync("Kein Launcher",
                $"Kein .sh/.exe im aktiven Sub-Ordner gefunden:\n{dir}");
            return;
        }
        try
        {
            var psi = new ProcessStartInfo(launcher)
            {
                WorkingDirectory = dir,
                UseShellExecute = true,
            };
            Process.Start(psi);
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
        {
            return Directory.EnumerateFiles(dir, "*.sh").FirstOrDefault();
        }
        return Directory.EnumerateFiles(dir, "*.exe")
            .Where(f => !f.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    [RelayCommand]
    private async Task RemoveAsync(GameRow? row)
    {
        if (row is null) return;
        var ok = await _host.Dialogs.ConfirmAsync("Aus Registry entfernen",
            $"„{row.DisplayName}\" wird aus games.json entfernt. Der Ordner auf der " +
            $"Festplatte bleibt unberührt.\n\nEntfernen?");
        if (!ok) return;
        _registry.Remove(row.Game.ContainerPath);
    }
}
