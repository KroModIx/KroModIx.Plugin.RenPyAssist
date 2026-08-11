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

    // In-flight-Dedup: verhindert dass 6 parallel gerenderte Views 6 mal
    // dieselbe URL downloaden (Bandbreite sparen + Race-Warnings vermeiden).
    // Key = coverUrl. Wert = laufender Download-Task, alle Caller warten
    // auf dieselbe Task-Instanz. Nach Completion aus dem Dict raus.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<string?>> _inFlight = new();

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
    /// geladen. Rückgabe: Pfad zur lokalen Datei oder null. Concurrent
    /// Calls für dieselbe URL warten auf einen einzigen Download.</summary>
    public Task<string?> EnsureAsync(string coverUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(coverUrl)) return Task.FromResult<string?>(null);
        return _inFlight.GetOrAdd(coverUrl, url => EnsureInternalAsync(url, ct)
            .ContinueWith(t =>
            {
                _inFlight.TryRemove(url, out _);
                return t.Result;
            }, TaskContinuationOptions.ExecuteSynchronously));
    }

    private async Task<string?> EnsureInternalAsync(string coverUrl, CancellationToken ct)
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

        // AVIF/WebP → PNG. F95zone imgproxy transkodiert alle Cover zu
        // AVIF egal welche URL-Extension. ImageSharp 3.x kann kein AVIF
        // (4.x ist kommerziell). Fallback-Kaskade:
        //   1) ImageSharp (WebP, ggf. PNG/JPEG-Recode)
        //   2) ffmpeg via Process (AVIF-Standard-Fallback, Linux idR da)
        // Wenn beide fehlschlagen: null zurückgeben (kein Cache-Eintrag).
        if (IsAvif(bytes) || IsWebp(bytes))
        {
            var converted = TryConvertToPng(bytes);
            if (converted is null && IsAvif(bytes))
            {
                Log.Debug("ImageSharp konnte AVIF nicht dekodieren, versuche ffmpeg-Fallback");
                converted = await TryConvertWithFfmpegAsync(bytes, ".avif", ct);
            }
            if (converted is not null)
            {
                bytes = converted;
            }
            else
            {
                Log.Warn("AVIF/WebP-Decode fehlgeschlagen — Cover nicht cachbar: {Url}", coverUrl);
                return null;
            }
        }
        // Animierte GIFs (v0.8.2): manche f95zone-Cover sind animierte GIFs
        // mit 10+ MB. Avalonia's Bitmap-Ctor kann sie nicht dekodieren, und
        // sie fressen RAM. Erstes Frame via ffmpeg extrahieren → PNG,
        // deutlich kleiner + Avalonia-safe. Statische GIFs (< 500 KB)
        // reichen wir durch — Avalonia's Bitmap kommt mit denen klar.
        else if (IsGif(bytes) && bytes.Length > 500 * 1024)
        {
            Log.Debug("Grosses GIF ({KB} KB) — first-frame-Convert via ffmpeg", bytes.Length / 1024);
            var converted = await TryConvertWithFfmpegAsync(bytes, ".gif", ct);
            if (converted is not null) bytes = converted;
            // Fallback: Original-GIF durchreichen — Bitmap-Ctor entscheidet
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

    /// <summary>Konvertiert Bild-Bytes zu PNG via ffmpeg (falls installiert).
    /// Nutze das für AVIF (ImageSharp 3.x kann kein AVIF, 4.x ist kommerziell)
    /// und für animierte GIFs (Avalonia's Bitmap-Ctor kann keine großen GIFs).
    /// Bei GIF wird nur das erste Frame extrahiert (<c>-vframes 1</c>).
    /// Umweg über zwei Temp-Files statt stdin/stdout-Pipe: bei letzterer
    /// gab's im .NET-Pipe-Wrapper partial-Write-Bugs bei ~300 KB Files.
    /// Auf Linux (Bazzite/Fedora) ist ffmpeg praktisch immer da; auf
    /// Windows muss der User es installieren (choco/winget). Bei fehlendem
    /// ffmpeg: return null, kein Crash.</summary>
    private static async Task<byte[]?> TryConvertWithFfmpegAsync(byte[] sourceBytes, string inputExt, CancellationToken ct)
    {
        string inPath = Path.Combine(Path.GetTempPath(), $"renpyassist-cover-{Guid.NewGuid():N}{inputExt}");
        string outPath = Path.Combine(Path.GetTempPath(), $"renpyassist-cover-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(inPath, sourceBytes, ct);
            var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(inPath);
            // Bei animierten Formaten (GIF/APNG): nur erstes Frame extrahieren.
            if (string.Equals(inputExt, ".gif", StringComparison.OrdinalIgnoreCase))
            {
                psi.ArgumentList.Add("-vframes"); psi.ArgumentList.Add("1");
            }
            psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("png");
            psi.ArgumentList.Add(outPath);

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            var errTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0 || !File.Exists(outPath))
            {
                Log.Warn("ffmpeg Cover-Convert ({Ext}) exit={Code}, stderr: {Err}",
                    inputExt, proc.ExitCode, await errTask);
                return null;
            }
            return await File.ReadAllBytesAsync(outPath, ct);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ffmpeg nicht im PATH — silently, kein Cover.
            Log.Debug("ffmpeg nicht installiert — Cover ({Ext}) kann nicht konvertiert werden", inputExt);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "ffmpeg AVIF-Convert Ausnahme");
            return null;
        }
        finally
        {
            try { if (File.Exists(inPath)) File.Delete(inPath); } catch { }
            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
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

    /// <summary>GIF-Magic: <c>GIF87a</c> oder <c>GIF89a</c>.</summary>
    private static bool IsGif(byte[] b)
    {
        return b.Length >= 6
            && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38
            && (b[4] == 0x37 || b[4] == 0x39) && b[5] == 0x61;
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
