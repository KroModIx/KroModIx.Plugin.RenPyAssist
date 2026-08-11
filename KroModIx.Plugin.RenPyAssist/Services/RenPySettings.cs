using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Plugin-Settings — persistent in <c>settings.json</c>.</summary>
public sealed class RenPySettings
{
    /// <summary>Root-Ordner mit den Ren'Py-Container-Ordnern. Wird vom
    /// User im Settings-Tab gesetzt.</summary>
    [JsonPropertyName("gamesRoot")]
    public string GamesRoot { get; set; } = "";

    /// <summary>Zusätzlicher Downloads-Ordner der überwacht wird (default:
    /// <c>~/Downloads</c> auf Linux, <c>%USERPROFILE%\Downloads</c> auf Windows).</summary>
    [JsonPropertyName("downloadsWatchDir")]
    public string? DownloadsWatchDir { get; set; }

    /// <summary>Wie oft der Worker f95zone-Threads auf neue Versionen prüft.
    /// Default 60 Minuten (Rate-Limit-Ruecksicht). Werte: 15/30/60/240
    /// (Empfehlung 60).</summary>
    [JsonPropertyName("checkIntervalMinutes")]
    public int CheckIntervalMinutes { get; set; } = 60;

    /// <summary>F95zone-Login-User (nur User-Name, Cookies extern
    /// verschluesselt im Host-Secrets-Store).</summary>
    [JsonPropertyName("f95Username")]
    public string F95Username { get; set; } = "";
}

/// <summary>Loader/Saver mit atomarem File-Move.</summary>
public sealed class RenPySettingsService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly RenPyPaths _paths;

    public RenPySettingsService(RenPyPaths paths)
    {
        _paths = paths;
        Current = Load() ?? new RenPySettings
        {
            DownloadsWatchDir = DefaultDownloadsDir(),
        };
    }

    public RenPySettings Current { get; private set; }

    public void Save(RenPySettings settings)
    {
        Current = settings;
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _paths.SettingsPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_paths.SettingsPath)) File.Delete(_paths.SettingsPath);
            File.Move(tmp, _paths.SettingsPath);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Settings-Save fehlgeschlagen");
        }
    }

    private RenPySettings? Load()
    {
        try
        {
            if (!File.Exists(_paths.SettingsPath)) return null;
            return JsonSerializer.Deserialize<RenPySettings>(File.ReadAllText(_paths.SettingsPath));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Settings-Load fehlgeschlagen");
            return null;
        }
    }

    private static string DefaultDownloadsDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Downloads");
    }
}
