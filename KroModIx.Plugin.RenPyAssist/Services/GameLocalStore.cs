using System;
using System.IO;
using System.Text.Json;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Speichert die per-Spiel-Metadata (Thread-URL, Beschreibung, Genre,
/// KI-Übersetzungen, Cover-Bild) direkt im Container-Ordner unter
/// <c>&lt;Container&gt;/.renpyassist/</c>. Vorteil: wenn der User seine
/// Ren'Py-Sammlung auf einen anderen PC überträgt oder umbenennt, kommt die
/// Config automatisch mit. Der zentrale <c>games.json</c> im Plugin-Data-Dir
/// bleibt als Index (welche Container hat das Plugin schon gesehen).
///
/// <para>Layout:</para>
/// <list type="bullet">
/// <item><c>.renpyassist/game.json</c> — komplette <see cref="RenPyGame"/>-
///   Serialisierung (ohne <c>ContainerPath</c>, der ist der Ordner selbst).</item>
/// <item><c>.renpyassist/cover.img</c> — Cover-Bild (PNG/JPEG nach ffmpeg-
///   Convert von AVIF), Fallback für Sidebar-Kachel wenn Plugin-Cache leer.</item>
/// </list></summary>
public sealed class GameLocalStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private const string StoreDir = ".renpyassist";
    private const string GameJson = "game.json";
    private const string CoverFile = "cover.img";
    private const string SidebarCoverFile = "sidebar-cover.png";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string StoreDirFor(string containerPath) =>
        Path.Combine(containerPath, StoreDir);

    public static string GameJsonPath(string containerPath) =>
        Path.Combine(StoreDirFor(containerPath), GameJson);

    public static string CoverPath(string containerPath) =>
        Path.Combine(StoreDirFor(containerPath), CoverFile);

    /// <summary>Sidebar-spezifischer Ausschnitt (2:3 Portrait, PNG). Wird vom
    /// <see cref="Views.CoverCropDialog"/> geschrieben und via
    /// <c>IHostServices.TrySetManualGameCover</c> als Sidebar-Kachel gesetzt.</summary>
    public static string SidebarCoverPath(string containerPath) =>
        Path.Combine(StoreDirFor(containerPath), SidebarCoverFile);

    /// <summary>Liest die lokale Config. Null wenn keine da (neuer Container).</summary>
    public static RenPyGame? Load(string containerPath)
    {
        var path = GameJsonPath(containerPath);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var game = JsonSerializer.Deserialize<RenPyGame>(json);
            if (game is null) return null;
            game.ContainerPath = containerPath; // Aus dem Ordner-Kontext restauriert
            return game;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Container-Local-Config unlesbar: {Path}", path);
            return null;
        }
    }

    public static void Save(RenPyGame game)
    {
        if (string.IsNullOrEmpty(game.ContainerPath) || !Directory.Exists(game.ContainerPath))
            return;
        try
        {
            var dir = StoreDirFor(game.ContainerPath);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, GameJson);
            var tmp = path + ".tmp";
            var json = JsonSerializer.Serialize(game, JsonOpts);
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Container-Local-Save fehlgeschlagen: {Container}", game.ContainerPath);
        }
    }

    /// <summary>Kopiert das gecachte Cover in den Container-Store. Nutzung:
    /// nachdem CoverCache.EnsureAsync fertig ist, spiegelt der Plugin das
    /// Cover in den Container damit es beim Ordner-Umzug mitwandert.</summary>
    public static string? CopyCoverIntoContainer(string containerPath, string cachedCoverPath)
    {
        if (string.IsNullOrEmpty(containerPath) || !Directory.Exists(containerPath)) return null;
        if (string.IsNullOrEmpty(cachedCoverPath) || !File.Exists(cachedCoverPath)) return null;
        try
        {
            var dir = StoreDirFor(containerPath);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, CoverFile);
            File.Copy(cachedCoverPath, target, overwrite: true);
            return target;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Container-Cover-Copy fehlgeschlagen");
            return null;
        }
    }
}
