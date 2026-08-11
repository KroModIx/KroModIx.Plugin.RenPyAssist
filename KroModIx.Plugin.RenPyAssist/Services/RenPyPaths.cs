using System.IO;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Zentraler Pfad-Anbieter — Cache/Data/Downloads-Ordner unter
/// dem Plugin-Data-/Cache-Root vom Host.</summary>
public sealed class RenPyPaths
{
    private readonly IHostServices _host;

    public RenPyPaths(IHostServices host)
    {
        _host = host;
        Directory.CreateDirectory(GamesRegistryDir);
        Directory.CreateDirectory(CoverCacheDir);
    }

    public string PluginDataDir => _host.PluginDataDir;
    public string PluginCacheDir => _host.PluginCacheDir;

    /// <summary>Persistente <c>games.json</c> mit Registry-Einträgen.</summary>
    public string GamesRegistryDir => Path.Combine(_host.PluginDataDir, "registry");
    public string GamesRegistryPath => Path.Combine(GamesRegistryDir, "games.json");

    /// <summary>Cover-Cache (SHA256(URL) → PNG). Analog RenPack-CoverCache.</summary>
    public string CoverCacheDir => Path.Combine(_host.PluginCacheDir, "covers");

    /// <summary>F95zone-Session-Cookies (verschlüsselt via Host-Secrets).</summary>
    public string F95zoneCookiesPath => Path.Combine(_host.PluginDataDir, "f95zone-cookies.enc");

    /// <summary>Plugin-Settings (Root-Ordner, Downloads-Watch-Pfad).</summary>
    public string SettingsPath => Path.Combine(_host.PluginDataDir, "settings.json");
}
