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
        Screenshot = null;
        MetadataText = null;
        var dir = SavesDir;
        if (!Directory.Exists(dir))
        {
            StatusText = $"saves/-Ordner nicht gefunden: {dir}";
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
            StatusText = $"{Saves.Count} Save(s) im Ordner {dir}";
            if (Saves.Count > 0) SelectedSave = Saves[0];
        }
        finally { IsBusy = false; }
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
            StatusText = $"Lade Save: {row.FileName} …";
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
                ? $"{_allVariables.Count} Variable(n) editierbar"
                : $"Log-Fehler: {info.LogError}";
        }
        catch (Exception ex)
        {
            StatusText = "Lade-Fehler: " + ex.Message;
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

    private void LoadScreenshot(byte[] bytes)
    {
        try
        {
            using var s = new MemoryStream(bytes);
            var bmp = new Bitmap(s);
            Dispatcher.UIThread.Post(() => Screenshot = bmp);
        }
        catch { }
    }

    private static string FormatMetadata(SaveInfo info)
    {
        var m = info.Metadata;
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(m.SaveName)) lines.Add($"Slot: {m.SaveName}");
        if (m.SaveTime is not null) lines.Add($"Zeit: {m.SaveTime:yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrEmpty(m.GameName)) lines.Add($"Spiel: {m.GameName}");
        if (!string.IsNullOrEmpty(m.RenpyVersion)) lines.Add($"Ren'Py: {m.RenpyVersion}");
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
                await _host.Dialogs.ShowMessageAsync("Wert ungültig",
                    $"„{v.Name}\": „{v.EditedValue}\" ist kein gültiges Python-Literal.");
                return;
            }
            edits.Add(new SaveEdit(v.Name, parsed));
        }
        if (edits.Count == 0)
        {
            _host.Notifications.Notify("Keine Änderungen zum Speichern", NotificationLevel.Info);
            return;
        }
        var ok = await _host.Dialogs.ConfirmAsync("Save überschreiben?",
            $"{edits.Count} Variable(n) werden im Save „{SelectedSave.FileName}\" gepatched.\n" +
            $"Ren'Py-Saves werden byte-preserving editiert — Roundtrip-safe. Trotzdem: " +
            $"vorher Backup empfohlen.\n\nFortfahren?");
        if (!ok) return;
        try
        {
            IsBusy = true;
            await Task.Run(() => _saveService.Write(SelectedSave.FullPath, SelectedSave.FullPath, edits));
            _host.Notifications.Notify($"{edits.Count} Änderung(en) gespeichert",
                NotificationLevel.Success);
            foreach (var v in _allVariables) v.MarkSaved();
            await LoadSaveAsync(SelectedSave); // Refresh from disk
        }
        catch (Exception ex)
        {
            await _host.Dialogs.ShowMessageAsync("Save-Fehler", ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenSavesFolder() => _host.Shell.OpenDirectory(SavesDir);
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
