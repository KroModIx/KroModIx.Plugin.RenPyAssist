using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services.Preview;
using KroModIx.Plugin.RenPyAssist.Services.Rpa;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Archives-Tab: listet <c>.rpa</c>-Files im <c>game/</c>-Verzeichnis,
/// öffnet den Index, zeigt Datei-Baum mit Preview-Panel und Extract-Aktionen.</summary>
public sealed partial class ArchivesViewModel : ObservableObject
{
    private readonly string _containerPath;
    private readonly string? _activeSubPath;
    private readonly RenpyArchiveService _archives;
    private readonly MediaPreviewService _preview;
    private readonly IHostServices _host;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private ArchiveRow? _selectedArchive;
    [ObservableProperty] private EntryRow? _selectedEntry;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private string? _previewInfo;
    private bool _canPlayExternal;
    public bool CanPlayExternal => _canPlayExternal;

    public ObservableCollection<ArchiveRow> Archives { get; } = new();
    public ObservableCollection<EntryRow> Entries { get; } = new();

    public ArchivesViewModel(string containerPath, string? activeSubPath,
        RenpyArchiveService archives, MediaPreviewService preview, IHostServices host)
    {
        _containerPath = containerPath;
        _activeSubPath = activeSubPath;
        _archives = archives;
        _preview = preview;
        _host = host;
        _ = ScanAsync();
    }

    private string GameDir => string.IsNullOrEmpty(_activeSubPath)
        ? Path.Combine(_containerPath, "game")
        : Path.Combine(_containerPath, _activeSubPath!, "game");

    [RelayCommand]
    private async Task ScanAsync()
    {
        Archives.Clear();
        Entries.Clear();
        PreviewText = null; PreviewImage = null; PreviewInfo = null;
        var gameDir = GameDir;
        if (!Directory.Exists(gameDir))
        {
            StatusText = $"game/-Ordner nicht gefunden: {gameDir}";
            return;
        }
        try
        {
            IsBusy = true;
            var rpas = await Task.Run(() => Directory
                .EnumerateFiles(gameDir, "*.rpa", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList());
            foreach (var p in rpas)
                Archives.Add(new ArchiveRow(p, new FileInfo(p).Length));
            StatusText = $"{Archives.Count} .rpa-Archiv(e) gefunden";
            if (Archives.Count > 0) SelectedArchive = Archives[0];
        }
        finally { IsBusy = false; }
    }

    partial void OnSelectedArchiveChanged(ArchiveRow? value) => _ = LoadIndexAsync(value);

    private async Task LoadIndexAsync(ArchiveRow? row)
    {
        Entries.Clear();
        PreviewText = null; PreviewImage = null; PreviewInfo = null;
        if (row is null) return;
        try
        {
            IsBusy = true;
            StatusText = $"Lade Index: {row.FileName} …";
            var info = await Task.Run(() => _archives.ReadIndex(row.FullPath));
            row.Version = info.Version.ToDisplay();
            row.EntryCount = info.Entries.Count;
            row.TotalSize = info.TotalSize;
            row.Archive = info;
            foreach (var e in info.Entries.Take(5000))
                Entries.Add(new EntryRow(e));
            StatusText = $"{info.Version.ToDisplay()} · {info.Entries.Count} Datei(en) · " +
                $"{FormatSize(info.TotalSize)}";
        }
        catch (Exception ex)
        {
            StatusText = "Index-Fehler: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    partial void OnSelectedEntryChanged(EntryRow? value) => _ = LoadPreviewAsync(value);

    private async Task LoadPreviewAsync(EntryRow? row)
    {
        PreviewText = null; PreviewImage = null; PreviewInfo = null;
        _canPlayExternal = false;
        if (row is null || SelectedArchive?.Archive is null) return;
        var archivePath = SelectedArchive.FullPath;
        var entry = row.Entry;
        var kind = MediaPreviewService.Classify(entry.Path);
        PreviewInfo = $"{entry.Path} · {FormatSize(entry.Size)} · {kind}";
        try
        {
            IsBusy = true;
            long limit = kind switch
            {
                PreviewKind.Text => MediaPreviewService.TextMaxBytes,
                PreviewKind.Image => MediaPreviewService.ImageMaxBytes,
                PreviewKind.Video => MediaPreviewService.VideoMaxBytes,
                PreviewKind.Audio => MediaPreviewService.VideoMaxBytes,
                _ => 8 * 1024 * 1024,
            };
            var bytes = await Task.Run(() => _archives.ReadEntryBytes(archivePath, entry, limit));
            if (bytes is null)
            {
                PreviewInfo += " · zu groß für Preview";
                return;
            }
            switch (kind)
            {
                case PreviewKind.Text:
                    PreviewText = MediaPreviewService.DecodeText(bytes);
                    break;
                case PreviewKind.Image:
                    LoadBitmap(bytes);
                    break;
                case PreviewKind.Video:
                    var frame = await _preview.GrabFirstFrameAsync(bytes,
                        Path.GetExtension(entry.Path));
                    if (frame is not null) LoadBitmap(frame);
                    else PreviewInfo += " · Video-Frame-Grab fehlgeschlagen (ffmpeg?)";
                    PreviewInfo += " · Klick aufs Bild oder ▶ Extern öffnen für Playback";
                    _canPlayExternal = true;
                    break;
                case PreviewKind.Audio:
                    PreviewInfo += " · Audio — ▶ Extern öffnen für Playback";
                    _canPlayExternal = true;
                    break;
                case PreviewKind.Binary:
                    PreviewInfo += " · binär (kein Preview)";
                    break;
            }
        }
        catch (Exception ex)
        {
            PreviewInfo += " · Fehler: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void LoadBitmap(byte[] bytes)
    {
        try
        {
            using var s = new MemoryStream(bytes);
            var bmp = new Bitmap(s);
            Dispatcher.UIThread.Post(() => PreviewImage = bmp);
        }
        catch { PreviewInfo += " · Bild-Decode fehlgeschlagen"; }
    }

    [RelayCommand]
    private async Task OpenExternalAsync()
    {
        if (SelectedEntry is null || SelectedArchive?.Archive is null) return;
        var bytes = await Task.Run(() => _archives.ReadEntryBytes(
            SelectedArchive.FullPath, SelectedEntry.Entry, MediaPreviewService.VideoMaxBytes));
        if (bytes is null) return;
        var path = _preview.OpenExternal(bytes, Path.GetExtension(SelectedEntry.Entry.Path));
        if (path is not null)
            _host.Notifications.Notify($"Extern geöffnet: {Path.GetFileName(SelectedEntry.Entry.Path)}",
                NotificationLevel.Info);
    }

    [RelayCommand]
    private async Task ExtractSelectedAsync()
    {
        if (SelectedEntry is null || SelectedArchive?.Archive is null) return;
        var dir = await _host.Dialogs.PickFolderAsync("Zielordner für Extraktion");
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            IsBusy = true;
            await Task.Run(() =>
            {
                var target = Path.Combine(dir, Path.GetFileName(SelectedEntry.Entry.Path));
                _archives.ExtractEntry(SelectedArchive.FullPath, SelectedEntry.Entry, target);
            });
            _host.Notifications.Notify(
                $"Entpackt: {Path.GetFileName(SelectedEntry.Entry.Path)}",
                NotificationLevel.Success);
        }
        catch (Exception ex)
        {
            await _host.Dialogs.ShowMessageAsync("Extract-Fehler", ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ExtractAllAsync()
    {
        if (SelectedArchive?.Archive is null) return;
        var dir = await _host.Dialogs.PickFolderAsync("Zielordner für gesamtes Archiv");
        if (string.IsNullOrEmpty(dir)) return;
        var ok = await _host.Dialogs.ConfirmAsync("Alles entpacken?",
            $"{SelectedArchive.Archive.Entries.Count} Datei(en) werden nach\n{dir}\nentpackt. Fortfahren?");
        if (!ok) return;
        try
        {
            IsBusy = true;
            var count = await Task.Run(() => _archives.ExtractAll(SelectedArchive.Archive, dir));
            _host.Notifications.Notify(
                $"{count} Datei(en) aus {SelectedArchive.FileName} entpackt",
                NotificationLevel.Success);
        }
        catch (Exception ex)
        {
            await _host.Dialogs.ShowMessageAsync("Extract-Fehler", ex.Message);
        }
        finally { IsBusy = false; }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };
}

public sealed partial class ArchiveRow : ObservableObject
{
    public string FullPath { get; }
    public string FileName { get; }
    public long FileSize { get; }
    [ObservableProperty] private string _version = "…";
    [ObservableProperty] private int _entryCount;
    [ObservableProperty] private long _totalSize;
    public RpaArchiveInfo? Archive { get; set; }
    public string Summary => $"{FileName}  ·  {Version}  ·  {EntryCount} Files";

    public ArchiveRow(string fullPath, long fileSize)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
        FileSize = fileSize;
    }

    partial void OnVersionChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnEntryCountChanged(int value) => OnPropertyChanged(nameof(Summary));
}

public sealed class EntryRow
{
    public RpaEntry Entry { get; }
    public string DisplayPath => Entry.Path;
    public string SizeText { get; }
    public EntryRow(RpaEntry entry)
    {
        Entry = entry;
        SizeText = entry.Size switch
        {
            < 1024 => $"{entry.Size} B",
            < 1024 * 1024 => $"{entry.Size / 1024.0:F1} KB",
            _ => $"{entry.Size / (1024.0 * 1024):F1} MB",
        };
    }
}
