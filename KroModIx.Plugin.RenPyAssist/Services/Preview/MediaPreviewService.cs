using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services.Preview;

/// <summary>Kategorisiert eine Datei/Bytes anhand von Extension + Magic-Bytes
/// und liefert Preview-Daten für die UI. Nach Kroste-Standard (Spektiv/
/// FfmpegFrameGrabber): kein LibVLC, kein Managed-Codec-Aufwand — Video-
/// Thumbnails via ffmpeg-Subprocess, Playback von Video/Audio via
/// System-Default-Player.
///
/// <para>Portiert aus RenPack <c>MediaPlaybackService.cs</c>, angepasst auf
/// Byte-Input (Preview-Content aus RPA-Archiven).</para></summary>
public sealed class MediaPreviewService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string? FfmpegPath { get; } = ResolveFfmpeg();
    public bool HasFfmpeg => !string.IsNullOrEmpty(FfmpegPath);

    // Preview-Limits (RenPack-Konvention)
    public const int TextMaxBytes = 512 * 1024;      // 512 KB
    public const int ImageMaxBytes = 50 * 1024 * 1024;   // 50 MB
    public const int VideoMaxBytes = 500 * 1024 * 1024;  // 500 MB

    public static PreviewKind Classify(string entryPath)
    {
        var ext = Path.GetExtension(entryPath).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "rpy" or "txt" or "json" or "py" or "xml" or "yml" or "yaml"
                or "md" or "cfg" or "ini" or "log" or "csv" or "html" or "css"
                or "js" or "info" or "nfo" or "readme" => PreviewKind.Text,
            "png" or "jpg" or "jpeg" or "webp" or "bmp" or "gif" or "ico"
                or "tga" or "tiff" => PreviewKind.Image,
            "mp4" or "mkv" or "webm" or "avi" or "mov" or "ogv" or "m4v" => PreviewKind.Video,
            "mp3" or "ogg" or "opus" or "wav" or "flac" or "m4a" or "aac" => PreviewKind.Audio,
            _ => PreviewKind.Binary,
        };
    }

    /// <summary>Text-Preview aus Bytes — versucht UTF-8, Fallback Latin-1.</summary>
    public static string? DecodeText(byte[] bytes)
    {
        if (bytes.Length > TextMaxBytes) return null;
        try { return Encoding.UTF8.GetString(bytes); }
        catch
        {
            try { return Encoding.Latin1.GetString(bytes); }
            catch { return null; }
        }
    }

    /// <summary>Video-Standbild als JPEG-Bytes via ffmpeg. Braucht temp-File
    /// als Input weil ffmpeg pipes bei manchen Codecs stolpert.</summary>
    public async Task<byte[]?> GrabFirstFrameAsync(byte[] videoBytes, string ext, CancellationToken ct = default)
    {
        if (!HasFfmpeg || videoBytes.Length == 0) return null;

        string inTmp = Path.Combine(Path.GetTempPath(), $"renpyassist-{Guid.NewGuid():N}{ext}");
        string outTmp = Path.Combine(Path.GetTempPath(), $"renpyassist-frame-{Guid.NewGuid():N}.jpg");
        try
        {
            await File.WriteAllBytesAsync(inTmp, videoBytes, ct);
            var psi = new ProcessStartInfo(FfmpegPath!)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(inTmp);
            psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-q:v"); psi.ArgumentList.Add("4");
            psi.ArgumentList.Add(outTmp);

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await proc.WaitForExitAsync(timeout.Token);
            if (proc.ExitCode != 0 || !File.Exists(outTmp))
            {
                Log.Debug("ffmpeg frame-grab exit={Code}", proc.ExitCode);
                return null;
            }
            return await File.ReadAllBytesAsync(outTmp, ct);
        }
        catch (Exception ex) { Log.Debug(ex, "GrabFirstFrame failed"); return null; }
        finally
        {
            try { if (File.Exists(inTmp)) File.Delete(inTmp); } catch { }
            try { if (File.Exists(outTmp)) File.Delete(outTmp); } catch { }
        }
    }

    /// <summary>Schreibt Bytes in eine Temp-Datei und öffnet sie mit dem
    /// System-Default-Player (Video/Audio). Rückgabe: der Temp-Pfad (bleibt
    /// liegen für Playback-Dauer; Caller sollte selbst aufräumen).</summary>
    public string? OpenExternal(byte[] bytes, string ext)
    {
        try
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"renpyassist-play-{Guid.NewGuid():N}{ext}");
            File.WriteAllBytes(tmp, bytes);
            Process.Start(new ProcessStartInfo(tmp) { UseShellExecute = true });
            return tmp;
        }
        catch (Exception ex) { Log.Warn(ex, "OpenExternal failed"); return null; }
    }

    private static string? ResolveFfmpeg()
    {
        var envPath = Environment.GetEnvironmentVariable("RENPYASSIST_FFMPEG");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath)) return envPath;
        // Standard-Kandidaten
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "ffmpeg.exe" }
            : new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg" };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        // PATH-Suche via `which`/`where`
        try
        {
            var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "where" : "which")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("ffmpeg");
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(2000);
            var first = output.Split('\n')[0].Trim();
            return File.Exists(first) ? first : null;
        }
        catch { return null; }
    }
}

public enum PreviewKind
{
    Text,
    Image,
    Video,
    Audio,
    Binary,
}
