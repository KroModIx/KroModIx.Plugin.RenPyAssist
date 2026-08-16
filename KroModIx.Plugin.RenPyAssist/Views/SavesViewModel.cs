using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;
using KroModIx.Plugin.RenPyAssist.Services.Saves;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Saves-Tab: listet <c>game/saves/*.save</c>-Files, öffnet den
/// ausgewählten Save via <see cref="RenpySaveService"/> und stellt Metadata +
/// Screenshot + editierbare Variablen dar. Änderungen werden über
/// <see cref="PicklePatcher"/> byte-preserving zurückgeschrieben.</summary>
public sealed partial class SavesViewModel : ObservableObject
{
    private readonly string _containerPath;
    private readonly string? _activeSubPath;
    private readonly RenpySaveService _saveService;
    private readonly IHostServices _host;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private SaveRow? _selectedSave;
    [ObservableProperty] private Bitmap? _screenshot;
    [ObservableProperty] private string? _metadataText;
    [ObservableProperty] private string _search = "";

    public ObservableCollection<SaveRow> Saves { get; } = new();
    public ObservableCollection<VariableRow> Variables { get; } = new();
    /// <summary>v0.11.1: chronologische Screenshot-Timeline (Auto→Slot1→…),
    /// gerendert als horizontale Thumbnail-Leiste unten im Save-Editor.
    /// Klick auf Thumbnail selektiert den Save.</summary>
    public ObservableCollection<TimelineEntry> Timeline { get; } = new();

    private List<VariableRow> _allVariables = new();

    public SavesViewModel(string containerPath, string? activeSubPath,
        RenpySaveService saveService, IHostServices host)
    {
        _containerPath = containerPath;
        _activeSubPath = activeSubPath;
        _saveService = saveService;
        _host = host;
        _ = ScanAsync();
    }

    private string SavesDir => string.IsNullOrEmpty(_activeSubPath)
        ? Path.Combine(_containerPath, "game", "saves")
        : Path.Combine(_containerPath, _activeSubPath!, "game", "saves");

    [RelayCommand]
    private async Task ScanAsync()
    {
        Saves.Clear();
        Variables.Clear();
        Timeline.Clear();
        Screenshot = null;
        MetadataText = null;
        var dir = SavesDir;
        if (!Directory.Exists(dir))
        {
            StatusText = string.Format(Strings.T("status.saves_dir_missing"), dir);
            return;
        }
        try
        {
            IsBusy = true;
            var files = await Task.Run(() => Directory
                .EnumerateFiles(dir, "*.save", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList());
            foreach (var f in files)
                Saves.Add(new SaveRow(f, new FileInfo(f)));
            StatusText = string.Format(Strings.T("status.saves_count"), Saves.Count, dir);
            if (Saves.Count > 0) SelectedSave = Saves[0];

            // v0.11.1: Timeline chronologisch (aelteste links → neuste rechts).
            // Screenshot-Extraktion parallel im Hintergrund — der User sieht
            // die Save-Liste sofort, Thumbnails plobben nach.
            _ = LoadTimelineAsync(files);
        }
        finally { IsBusy = false; }
    }

    /// <summary>v0.11.1: extrahiert Screenshots aller Saves im Hintergrund
    /// und baut die Timeline chronologisch auf. Fehler pro Save werden
    /// stillschweigend ignoriert (broken save → kein Thumbnail).</summary>
    private async Task LoadTimelineAsync(List<string> files)
    {
        // Chronologisch: aelteste zuerst (mtime ascending).
        var chronological = files
            .Select(p => new FileInfo(p))
            .OrderBy(fi => fi.LastWriteTimeUtc)
            .ToList();
        // SemaphoreSlim(4) — Pickle-Deserializer ist CPU-lastig, mehr Parallelitaet
        // hilft nix und macht die App unresponsive.
        using var gate = new System.Threading.SemaphoreSlim(4);
        var tasks = chronological.Select(async fi =>
        {
            await gate.WaitAsync();
            try
            {
                var (bytes, saveTime) = await Task.Run(() =>
                {
                    try
                    {
                        var info = _saveService.Read(fi.FullName);
                        return (info.ScreenshotBytes, info.Metadata.SaveTime);
                    }
                    catch { return (null, null); }
                });
                if (bytes is null) return (SaveRow?)null;
                Bitmap? thumb = null;
                // v0.15.0: Host-Baukasten IHostServices.Images (Contracts v1.18+)
                // fuer Screenshot-Thumbnails — Ren'Py-Saves koennen JPEG oder PNG
                // enthalten, Format-Detection macht der Host.
                try { thumb = await _host.Images.DecodeAsync(bytes); }
                catch { return null; }
                if (thumb is null) return null;
                var row = Saves.FirstOrDefault(r =>
                    string.Equals(r.FullPath, fi.FullName, StringComparison.Ordinal));
                if (row is null || thumb is null) return null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var entry = new TimelineEntry(row, thumb,
                        saveTime?.LocalDateTime ?? fi.LastWriteTime);
                    Timeline.Add(entry);
                });
                return row;
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    partial void OnSelectedSaveChanged(SaveRow? value) => _ = LoadSaveAsync(value);

    private async Task LoadSaveAsync(SaveRow? row)
    {
        Variables.Clear();
        _allVariables = new();
        Screenshot = null;
        MetadataText = null;
        if (row is null) return;
        try
        {
            IsBusy = true;
            StatusText = string.Format(Strings.T("status.loading_save"), row.FileName);
            var info = await Task.Run(() => _saveService.Read(row.FullPath));
            if (info.ScreenshotBytes is not null)
                LoadScreenshot(info.ScreenshotBytes);
            MetadataText = FormatMetadata(info);
            _allVariables = info.Variables
                .Where(v => !v.IsInternal || v.Name.StartsWith("_persistent"))
                .Select(v => new VariableRow(v))
                .ToList();
            ApplyFilter();
            StatusText = info.LogError is null
                ? string.Format(Strings.T("status.vars_editable"), _allVariables.Count)
                : string.Format(Strings.T("status.log_error"), info.LogError);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.T("status.load_error"), ex.Message);
        }
        finally { IsBusy = false; }
    }

    partial void OnSearchChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Variables.Clear();
        var filter = Search?.Trim() ?? "";
        var matches = filter.Length == 0
            ? _allVariables
            : _allVariables.Where(v => v.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var v in matches) Variables.Add(v);
    }

    private async void LoadScreenshot(byte[] bytes)
    {
        try
        {
            // v0.15.0: Host-Baukasten IHostServices.Images (Contracts v1.18+).
            var bmp = await _host.Images.DecodeAsync(bytes);
            if (bmp is null) return;
            await Dispatcher.UIThread.InvokeAsync(() => Screenshot = bmp);
        }
        catch { }
    }

    private static string FormatMetadata(SaveInfo info)
    {
        var m = info.Metadata;
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(m.SaveName)) lines.Add(string.Format(Strings.T("saves.meta.slot"), m.SaveName));
        if (m.SaveTime is not null) lines.Add(string.Format(Strings.T("saves.meta.time"),
            m.SaveTime.Value.ToString("yyyy-MM-dd HH:mm:ss")));
        if (!string.IsNullOrEmpty(m.GameName)) lines.Add(string.Format(Strings.T("saves.meta.game"), m.GameName));
        if (!string.IsNullOrEmpty(m.RenpyVersion)) lines.Add(string.Format(Strings.T("saves.meta.renpy"), m.RenpyVersion));
        return string.Join("  ·  ", lines);
    }

    [RelayCommand]
    private async Task SaveEditsAsync()
    {
        if (SelectedSave is null) return;
        var edits = new List<SaveEdit>();
        foreach (var v in _allVariables)
        {
            if (!v.HasUnsavedChanges) continue;
            if (!PythonLiteral.TryParse(v.EditedValue, out var parsed))
            {
                await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.value_invalid_title"),
                    string.Format(Strings.T("dialog.value_invalid_msg"), v.Name, v.EditedValue));
                return;
            }
            edits.Add(new SaveEdit(v.Name, parsed));
        }
        if (edits.Count == 0)
        {
            _host.Notifications.Notify(Strings.T("notify.saves_no_changes"), NotificationLevel.Info);
            return;
        }
        var ok = await _host.Dialogs.ConfirmAsync(Strings.T("dialog.save_overwrite_title"),
            string.Format(Strings.T("dialog.save_overwrite_msg"), edits.Count, SelectedSave.FileName));
        if (!ok) return;
        try
        {
            IsBusy = true;
            await Task.Run(() => _saveService.Write(SelectedSave.FullPath, SelectedSave.FullPath, edits));
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.saves_changes_saved"), edits.Count),
                NotificationLevel.Success);
            foreach (var v in _allVariables) v.MarkSaved();
            await LoadSaveAsync(SelectedSave); // Refresh from disk
        }
        catch (Exception ex)
        {
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.save_error_title"), ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenSavesFolder() => _host.Shell.OpenDirectory(SavesDir);

    /// <summary>v0.11.1: Klick auf ein Timeline-Thumbnail selektiert den
    /// zugehoerigen Save (das Screenshot-Bild + Variablen laden dann normal
    /// via OnSelectedSaveChanged).</summary>
    [RelayCommand]
    private void SelectFromTimeline(TimelineEntry? entry)
    {
        if (entry is null) return;
        SelectedSave = entry.Save;
    }
}

/// <summary>v0.11.1: ein Eintrag in der Screenshot-Timeline. Bildet einen
/// Save chronologisch ab (Thumbnail + Zeitstempel + Referenz auf SaveRow).</summary>
public sealed class TimelineEntry
{
    public SaveRow Save { get; }
    public Bitmap Thumbnail { get; }
    public string TimeText { get; }
    public string SlotName => Save.FileName;

    public TimelineEntry(SaveRow save, Bitmap thumbnail, DateTime saveTime)
    {
        Save = save;
        Thumbnail = thumbnail;
        TimeText = saveTime.ToString("dd.MM. HH:mm");
    }
}

public sealed class SaveRow
{
    public string FullPath { get; }
    public string FileName { get; }
    public string ModifiedText { get; }

    public SaveRow(string fullPath, FileInfo fi)
    {
        FullPath = fullPath;
        FileName = fi.Name;
        ModifiedText = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
    }
}

public sealed partial class VariableRow : ObservableObject
{
    public string Name { get; }
    public string TypeName { get; }
    private readonly string _originalValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _editedValue;

    public bool HasUnsavedChanges =>
        !string.Equals(EditedValue, _originalValue, StringComparison.Ordinal);

    public VariableRow(SaveVariable v)
    {
        Name = v.Name;
        TypeName = v.TypeName;
        _originalValue = v.Value;
        _editedValue = v.Value;
    }

    public void MarkSaved() => _originalValue.GetType(); // no-op — refresh from disk resets both
}
