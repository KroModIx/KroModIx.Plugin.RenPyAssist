using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Beobachtet den Downloads-Ordner via FileSystemWatcher und
/// feuert ein Event pro *fertig-geschriebener* ZIP-Datei.
///
/// <para><b>Warum Stability-Check:</b> FileSystemWatcher feuert während
/// eines Copy/Download mehrfach (Created dann n-mal Changed) — wir wollen
/// den User erst benachrichtigen wenn die ZIP fertig ist. Lösung: pro
/// erkanntem Pfad ein Timer, der bei jedem neuen Event auf 2 s zurück-
/// gestellt wird. Erst wenn 2 s ohne weiteres Event vergehen, gilt die
/// Datei als stabil und das Event feuert.</para></summary>
public sealed class DownloadWatcher : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private const int StabilityWindowMs = 2000;

    private readonly object _lock = new();
    private FileSystemWatcher? _fsw;
    private readonly Dictionary<string, Timer> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _detected = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Feuert wenn eine ZIP im überwachten Ordner „stabil" ist —
    /// 2 s nach dem letzten Change-Event. Callback läuft auf einem
    /// ThreadPool-Thread; Owner muss selbst zur UI dispatchen.</summary>
    public event Action<string>? StableZipDetected;

    public IReadOnlyCollection<string> Detected
    {
        get { lock (_lock) return _detected.ToList(); }
    }

    public int Count { get { lock (_lock) return _detected.Count; } }

    public void Start(string folder)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Log.Debug("DownloadWatcher: Ordner nicht vorhanden, übersprungen: {Folder}", folder);
            return;
        }
        try
        {
            _fsw = new FileSystemWatcher(folder, "*.zip")
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
            };
            _fsw.Created += OnFsEvent;
            _fsw.Changed += OnFsEvent;
            _fsw.Renamed += OnFsRenamed;
            Log.Info("DownloadWatcher: aktiv auf {Folder}", folder);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "DownloadWatcher konnte nicht gestartet werden: {Folder}", folder);
        }
    }

    public void Stop()
    {
        var fsw = _fsw;
        _fsw = null;
        if (fsw is not null)
        {
            try { fsw.EnableRaisingEvents = false; } catch { }
            fsw.Created -= OnFsEvent;
            fsw.Changed -= OnFsEvent;
            fsw.Renamed -= OnFsRenamed;
            fsw.Dispose();
        }
        lock (_lock)
        {
            foreach (var t in _pending.Values) t.Dispose();
            _pending.Clear();
        }
    }

    public void ClearDetected()
    {
        lock (_lock) _detected.Clear();
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => Schedule(e.FullPath);
    private void OnFsRenamed(object sender, RenamedEventArgs e) => Schedule(e.FullPath);

    private void Schedule(string fullPath)
    {
        if (!fullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;

        lock (_lock)
        {
            if (_pending.TryGetValue(fullPath, out var existing))
            {
                existing.Change(StabilityWindowMs, Timeout.Infinite);
                return;
            }
            _pending[fullPath] = new Timer(_ =>
            {
                lock (_lock)
                {
                    if (_pending.TryGetValue(fullPath, out var t))
                    {
                        _pending.Remove(fullPath);
                        t.Dispose();
                    }
                }
                if (!File.Exists(fullPath)) return;
                lock (_lock)
                {
                    if (!_detected.Add(fullPath)) return;
                }
                Log.Info("DownloadWatcher: stabile ZIP → {Path}", fullPath);
                try { StableZipDetected?.Invoke(fullPath); }
                catch (Exception ex) { Log.Warn(ex, "StableZipDetected-Handler geworfen"); }
            }, null, StabilityWindowMs, Timeout.Infinite);
        }
    }

    public void Dispose() => Stop();
}
