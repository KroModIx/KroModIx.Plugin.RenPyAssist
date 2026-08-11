using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
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

    /// <summary>Streamt Video-Frames als JPEG-Bytes für Inline-Preview
    /// (kein Audio). Portiert 1:1 aus RenPack <c>MediaPlaybackService</c>:
    /// ffmpeg mit <c>-f image2pipe -vcodec mjpeg</c> liefert JPEG-Frames
    /// back-to-back durch stdout, <c>JpegStreamReader</c> parst SOI/EOI-
    /// Marker. <c>-re</c> throttelt gegen Wanduhr (sonst Turbo-Speed).
    /// Bewusst kein LibVLC — deployment-Chaos, Airspace-Probleme im UI.
    ///
    /// <para>Verhalten bei Abbruch (CancellationToken): ffmpeg wird gekilled,
    /// letzter yield-Frame beendet die Enumeration ordentlich.</para></summary>
    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        string videoPath, int fps = 12,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!HasFfmpeg || !File.Exists(videoPath)) yield break;
        if (fps < 1 || fps > 60) throw new ArgumentOutOfRangeException(nameof(fps));

        var psi = new ProcessStartInfo(FfmpegPath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-nostdin");
        // -re throttelt Input gegen die Wall-Clock (native Frame-Rate).
        // Ohne das pumpt ffmpeg so schnell wie die Pipe schluckt →
        // Turbo-Speed-Playback. -re MUSS vor -i stehen (Input-Option).
        psi.ArgumentList.Add("-re");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(videoPath);
        psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add($"fps={fps}");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("image2pipe");
        psi.ArgumentList.Add("-vcodec"); psi.ArgumentList.Add("mjpeg");
        psi.ArgumentList.Add("-q:v"); psi.ArgumentList.Add("6"); // 2 (best) .. 31 (worst)
        psi.ArgumentList.Add("-");

        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception ex) { Log.Warn(ex, "ffmpeg-Stream start fehlgeschlagen"); yield break; }
        if (proc is null) yield break;

        try
        {
            await foreach (var jpeg in JpegStreamReader.ReadAsync(proc.StandardOutput.BaseStream, ct))
                yield return jpeg;
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            try { proc.Dispose(); } catch { }
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

    /// <summary>Liest einen MJPEG-Stream (JPEG-Frames back-to-back) frame-
    /// für-frame. Nutzt SOI (0xFF 0xD8) / EOI (0xFF 0xD9)-Marker um Frame-
    /// Grenzen zu finden. Portiert aus RenPack.</summary>
    internal static class JpegStreamReader
    {
        private const byte Marker = 0xFF;
        private const byte Soi = 0xD8;
        private const byte Eoi = 0xD9;
        private const int ReadBufferSize = 64 * 1024;

        public static async IAsyncEnumerable<byte[]> ReadAsync(
            Stream input, [EnumeratorCancellation] CancellationToken ct)
        {
            var buffer = new List<byte>(ReadBufferSize * 2);
            var read = new byte[ReadBufferSize];
            bool eofReached = false;

            while (true)
            {
                while (TryExtractFrame(buffer, out var frame))
                    yield return frame;

                if (eofReached) yield break;

                ct.ThrowIfCancellationRequested();
                int n;
                try { n = await input.ReadAsync(read.AsMemory(), ct); }
                catch (OperationCanceledException) { yield break; }
                if (n <= 0) { eofReached = true; continue; }

                for (int i = 0; i < n; i++) buffer.Add(read[i]);
            }
        }

        private static bool TryExtractFrame(List<byte> buffer, out byte[] frame)
        {
            frame = Array.Empty<byte>();
            int soiPos = -1;
            for (int i = 0; i < buffer.Count - 1; i++)
                if (buffer[i] == Marker && buffer[i + 1] == Soi) { soiPos = i; break; }
            if (soiPos < 0) return false;

            int eoiPos = -1;
            for (int i = soiPos + 2; i < buffer.Count - 1; i++)
                if (buffer[i] == Marker && buffer[i + 1] == Eoi) { eoiPos = i; break; }
            if (eoiPos < 0) return false;

            int frameLen = eoiPos + 2 - soiPos;
            frame = new byte[frameLen];
            buffer.CopyTo(soiPos, frame, 0, frameLen);
            buffer.RemoveRange(0, eoiPos + 2);
            return true;
        }
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
