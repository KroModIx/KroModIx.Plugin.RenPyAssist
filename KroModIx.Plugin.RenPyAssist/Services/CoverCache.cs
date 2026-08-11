using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Persistenter Cover-Bild-Cache. Files landen als
/// <c>&lt;sha256(url)&gt;.img</c> im <see cref="RenPyPaths.CoverCacheDir"/>.
/// Keine Extension-Auswertung — Avalonia's <c>Bitmap</c> erkennt PNG/JPG/WebP
/// per Magic-Bytes selbst.
///
/// <para><b>AVIF/WebP:</b> F95zones imgproxy transkodiert Original-PNGs zu
/// AVIF. Avaloia/Skia versteht AVIF nicht — deshalb konvertieren wir via
/// SixLabors.ImageSharp zu PNG bevor wir cachen. Vorteil gegenüber ffmpeg
/// (RenPack-Ansatz): kein externer Prozess, keine PATH-Abhängigkeit,
/// funktioniert im AppImage.</para>
///
/// <para><b>Kein Cache-Invalidation</b> — Cover ändern sich praktisch nie.
/// Wenn F95zone das Cover updated, kommt eine neue Attachment-ID, also
/// anderer Hash, neuer Cache-Eintrag. Zum manuellen Cleanup gibt's
/// <see cref="Purge"/>.</para></summary>
public sealed class CoverCache
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _cacheDir;
    private readonly F95zoneClient _client;

    public CoverCache(string cacheDir, F95zoneClient client)
    {
        _cacheDir = cacheDir;
        _client = client;
        Directory.CreateDirectory(_cacheDir);
    }

    public string PathFor(string coverUrl) =>
        Path.Combine(_cacheDir, HashUrl(coverUrl) + ".img");

    public bool IsCached(string coverUrl) => File.Exists(PathFor(coverUrl));

    /// <summary>Stellt sicher dass das Cover lokal liegt. Bei Cache-Miss
    /// wird via <see cref="F95zoneClient"/> (mit Session-Cookies) nach-
    /// geladen. Rückgabe: Pfad zur lokalen Datei oder null.</summary>
    public async Task<string?> EnsureAsync(string coverUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(coverUrl)) return null;
        var path = PathFor(coverUrl);
        if (File.Exists(path))
        {
            if (IsValidImage(await File.ReadAllBytesAsync(path, ct))) return path;
            Log.Debug("Cache-Eintrag ungültig, wird neu geladen: {Path}", path);
            try { File.Delete(path); } catch { }
        }

        var bytes = await _client.DownloadImageAsync(coverUrl, ct);
        if (bytes is null || bytes.Length == 0)
        {
            Log.Debug("Cover-Download fehlgeschlagen: {Url}", coverUrl);
            return null;
        }

        // AVIF/WebP → PNG via ImageSharp. Fallback bei WebP: ImageSharp
        // ab 3.x kann WebP-Decode nativ; AVIF-Decode braucht ImageSharp
        // 4+ oder ist experimentell — bei Fehler geben wir das Original
        // durch (Avalonia versucht Skia dann selbst).
        if (IsAvif(bytes) || IsWebp(bytes))
        {
            var converted = TryConvertToPng(bytes);
            if (converted is not null) bytes = converted;
        }

        if (!IsValidImage(bytes))
        {
            Log.Warn("Cover-URL liefert kein Bild (evtl. Login-Wall/CDN-Redirect): {Url}", coverUrl);
            return null;
        }

        try
        {
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Cover-Cache-Save fehlgeschlagen: {Path}", path);
            return null;
        }
    }

    private static byte[]? TryConvertToPng(byte[] source)
    {
        try
        {
            using var img = Image.Load(source);
            using var ms = new MemoryStream();
            img.Save(ms, new PngEncoder());
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ImageSharp-Convert fehlgeschlagen — Original wird durchgereicht");
            return null;
        }
    }

    private static bool IsAvif(byte[] b)
    {
        if (b.Length < 12) return false;
        return b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70 // "ftyp"
            && b[8] == 0x61 && b[9] == 0x76 && b[10] == 0x69 && b[11] == 0x66; // "avif"
    }

    private static bool IsWebp(byte[] b)
    {
        return b.Length >= 12
            && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
            && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50;
    }

    /// <summary>Prüft die ersten Bytes gegen bekannte Image-Magic-Bytes:
    /// PNG (89 50 4E 47), JPEG (FF D8 FF), GIF (47 49 46 38), WebP
    /// (RIFF...WEBP), BMP (42 4D).</summary>
    private static bool IsValidImage(byte[] bytes)
    {
        if (bytes.Length < 4) return false;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true; // PNG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true; // JPEG
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return true; // GIF
        if (IsWebp(bytes)) return true;
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true; // BMP
        return false;
    }

    public void Purge()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_cacheDir, "*.img"))
                File.Delete(f);
        }
        catch (Exception ex) { Log.Warn(ex, "Cover-Cache purge fehlgeschlagen"); }
    }

    private static string HashUrl(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
