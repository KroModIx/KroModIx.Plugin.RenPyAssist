using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <param name="BytesAdded">Größe der nachgerüsteten Runtime, für die Meldung.</param>
public sealed record RuntimeFixResult(bool Success, long BytesAdded, string? Error)
{
    public static RuntimeFixResult Fail(string error) => new(false, 0, error);
}

/// <summary>Rüstet einem Windows-only-Ren'Py-Build die Linux-Runtime nach,
/// damit er nativ startet — ohne Wine, ohne Proton.
///
/// <para>Die drei Schritte stammen aus einem von Hand durchgeführten und
/// protokollierten Versuch (18.10.2026): Runtime aus dem offiziellen SDK
/// kopieren, Startskript anlegen, und — der Fallstrick — das Binary unter
/// dem Namen der <c>.py</c> des Spiels ablegen. <c>renpy.sh</c> sucht
/// <c>$LIB/$BASEFILE</c> und fällt auf <c>$LIB/renpy</c> zurück; das Binary
/// leitet den Namen seiner <c>.py</c> aber aus seinem <em>eigenen</em>
/// Dateinamen ab. Als <c>renpy</c> aufgerufen sucht es <c>renpy.py</c>, die
/// es in einem Spielordner nie gibt. Genau so machen es auch die offiziellen
/// Linux-Builds.</para>
///
/// <para><b>Was aus dem SDK gebraucht wird:</b> nur
/// <c>lib/py3-linux-x86_64/</c> und <c>renpy.sh</c> — rund 40 MB von 146.
/// Der Rest wird nicht einmal ausgepackt.</para></summary>
public sealed class LinuxRuntimeInstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Es gibt kein eigenes Linux-Paket — <c>renpy-X.Y.Z-linux.tar.bz2</c>
    /// antwortet mit 404, nur das SDK enthält die Linux-Runtime.</summary>
    private const string SdkUrlTemplate = "https://www.renpy.org/dl/{0}/renpy-{0}-sdk.tar.bz2";

    private readonly IHostServices _host;
    private readonly string _cacheRoot;

    public LinuxRuntimeInstaller(IHostServices host, string cacheRoot)
    {
        _host = host;
        _cacheRoot = cacheRoot;
    }

    /// <summary>Wo die ausgepackte Runtime einer Version liegt. Ein Cache je
    /// Version spart den 146-MB-Download beim nächsten Build mit derselben
    /// Ren'Py-Fassung — und davon gibt es viele.</summary>
    public string CacheDirFor(string version) => Path.Combine(_cacheRoot, "renpy-sdk", version);

    /// <summary>Prüft, ob der Build überhaupt nachrüstbar ist, und nennt
    /// sonst den Grund. Null = in Ordnung.</summary>
    public string? Veto(string buildDir, BuildInspection b)
    {
        if (!b.IsWindowsOnly) return Strings.T("runtime.veto_not_windows_only");
        if (string.IsNullOrWhiteSpace(b.RenPyVersion)) return Strings.T("runtime.veto_no_version");
        if (string.IsNullOrWhiteSpace(b.GameScriptName)) return Strings.T("runtime.veto_no_script");

        // Auf NTFS lassen sich keine Exec-Bits setzen — die Runtime waere
        // nach dem Kopieren nicht ausfuehrbar, und der Nutzer stuende vor
        // demselben „Keine Berechtigung" wie ohne Nachruesten. Dort ist
        // Proton der richtige Weg, nicht dieser.
        var fs = FilesystemOf(buildDir);
        if (fs is "ntfs" or "ntfs3" or "fuseblk" or "vfat" or "exfat")
            return string.Format(Strings.T("runtime.veto_filesystem"), fs);

        return null;
    }

    /// <summary>Das Dateisystem unter einem Pfad, über <c>findmnt</c>.
    /// Null, wenn es sich nicht bestimmen lässt — dann wird nicht blockiert,
    /// ein unbekanntes Dateisystem ist kein Beweis für ein schlechtes.</summary>
    public static string? FilesystemOf(string path)
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("findmnt")
            {
                ArgumentList = { "-no", "FSTYPE", "-T", path },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            var o = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);
            return string.IsNullOrWhiteSpace(o) ? null : o.ToLowerInvariant();
        }
        catch (Exception) { return null; }
    }

    /// <summary>Lädt das SDK (falls nicht im Cache) und rüstet die Runtime
    /// nach. <paramref name="melde"/> bekommt Zwischenstände für die UI.</summary>
    public async Task<RuntimeFixResult> FixAsync(string buildDir, BuildInspection b,
        Action<string>? melde = null, CancellationToken ct = default)
    {
        var veto = Veto(buildDir, b);
        if (veto is not null) return RuntimeFixResult.Fail(veto);

        var version = b.RenPyVersion!;
        var scriptName = b.GameScriptName!;
        var cache = CacheDirFor(version);
        var runtimeQuelle = Path.Combine(cache, "lib", "py3-linux-x86_64");
        var shQuelle = Path.Combine(cache, "renpy.sh");

        if (!Directory.Exists(runtimeQuelle) || !File.Exists(shQuelle))
        {
            var geladen = await EnsureSdkAsync(version, cache, melde, ct).ConfigureAwait(false);
            if (geladen is not null) return RuntimeFixResult.Fail(geladen);
        }
        else
        {
            Log.Info("Ren'Py-SDK {Version} aus dem Cache: {Dir}", version, cache);
        }

        try
        {
            melde?.Invoke(Strings.T("runtime.step_copy"));

            // 1. Runtime kopieren.
            var runtimeZiel = Path.Combine(buildDir, "lib", "py3-linux-x86_64");
            CopyDirectory(runtimeQuelle, runtimeZiel);

            // 2. Startskript unter dem Namen der .py des Spiels.
            var shZiel = Path.Combine(buildDir, scriptName + ".sh");
            File.Copy(shQuelle, shZiel, overwrite: true);

            // 3. Binary unter demselben Namen — der Fallstrick. Das Binary
            //    leitet die zu ladende .py aus seinem eigenen Namen ab.
            var binQuelle = Path.Combine(runtimeZiel, "renpy");
            if (!File.Exists(binQuelle))
                return RuntimeFixResult.Fail(Strings.T("runtime.fail_no_binary"));
            var binZiel = Path.Combine(runtimeZiel, scriptName);
            if (!string.Equals(binQuelle, binZiel, StringComparison.Ordinal))
                File.Copy(binQuelle, binZiel, overwrite: true);

            // 4. Exec-Bits — derselbe Mechanismus wie nach jedem Entpacken.
            //    Reihenfolge bewusst: erst kopieren, dann Bits setzen.
            var bits = ExecutableBits.Apply(buildDir);
            Log.Info("Linux-Runtime nachgeruestet: {Build} (Ren'Py {Version}, "
                + "Skript {Script}, +x auf {Bits} Datei(en))", buildDir, version, scriptName, bits);

            var groesse = DirectorySize(runtimeZiel);
            return new RuntimeFixResult(true, groesse, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Nachruesten der Linux-Runtime fehlgeschlagen: {Build}", buildDir);
            return RuntimeFixResult.Fail(ex.Message);
        }
    }

    /// <summary>Lädt das SDK und packt die zwei gebrauchten Pfade aus.
    /// Rückgabe null = erfolgreich, sonst der Fehlergrund.</summary>
    private async Task<string?> EnsureSdkAsync(string version, string cache,
        Action<string>? melde, CancellationToken ct)
    {
        var url = string.Format(SdkUrlTemplate, version);
        var tmp = Path.Combine(Path.GetTempPath(),
            $"renpy-sdk-{version}-{Guid.NewGuid():N}.tar.bz2");
        try
        {
            using var http = _host.CreateHttpClient("renpy-sdk");
            http.Timeout = TimeSpan.FromMinutes(20);

            // Erst fragen, ob es die Version gibt. Bei Fix- und
            // Nightly-Branches steht im Build eine Versionsnummer, zu der es
            // kein veroeffentlichtes SDK gibt — ein 404 nach 146 MB waere
            // eine teure Art, das zu erfahren.
            using (var head = new HttpRequestMessage(HttpMethod.Head, url))
            using (var probe = await http.SendAsync(head, ct).ConfigureAwait(false))
            {
                if (!probe.IsSuccessStatusCode)
                    return string.Format(Strings.T("runtime.fail_no_sdk"), version,
                        (int)probe.StatusCode);
            }

            melde?.Invoke(string.Format(Strings.T("runtime.step_download"), version));
            Log.Info("Lade Ren'Py-SDK {Version}: {Url}", version, url);

            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                       .ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode)
                    return string.Format(Strings.T("runtime.fail_download"), (int)resp.StatusCode);
                await using var netz = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var datei = File.Create(tmp);
                await netz.CopyToAsync(datei, ct).ConfigureAwait(false);
            }

            melde?.Invoke(Strings.T("runtime.step_unpack"));

            // Nur die zwei gebrauchten Pfade. Das SDK bringt ein eigenes
            // Wurzelverzeichnis mit (renpy-X.Y.Z-sdk/), deshalb der Vergleich
            // auf das Pfadende statt auf den Anfang.
            Directory.CreateDirectory(cache);
            var res = await Task.Run(() => _host.Archives.Extract(tmp, cache,
                new ArchiveExtractOptions(
                    StripPrefix: SdkRootPrefix(tmp),
                    Filter: k => k.Contains("/lib/py3-linux-x86_64/", StringComparison.OrdinalIgnoreCase)
                              || k.EndsWith("/renpy.sh", StringComparison.OrdinalIgnoreCase)
                              || k.Equals("renpy.sh", StringComparison.OrdinalIgnoreCase))), ct)
                .ConfigureAwait(false);

            if (res.SkippedUnsafe.Count > 0)
            {
                TryDeleteDir(cache);
                return string.Format(Strings.T("runtime.fail_unsafe"), res.SkippedUnsafe.Count);
            }
            if (res.Count == 0)
            {
                TryDeleteDir(cache);
                return Strings.T("runtime.fail_empty_sdk");
            }

            Log.Info("Ren'Py-SDK {Version} ausgepackt: {N} Datei(en) nach {Dir}",
                version, res.Count, cache);
            return null;
        }
        catch (OperationCanceledException) { TryDeleteDir(cache); throw; }
        catch (Exception ex)
        {
            Log.Warn(ex, "SDK-Download fehlgeschlagen: {Url}", url);
            TryDeleteDir(cache);
            return ex.Message;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* Temp darf bleiben */ }
        }
    }

    /// <summary>Das Wurzelverzeichnis im SDK-Archiv
    /// (<c>renpy-8.5.2-sdk/</c>), damit der Cache flach wird.
    ///
    /// <para>Das <b>häufigste</b> erste Segment, nicht das erste: ein
    /// Tar-Archiv kann mit Einträgen beginnen, die kein Verzeichnis
    /// bezeichnen. Null, wenn es keine gemeinsame Wurzel gibt.</para></summary>
    private string? SdkRootPrefix(string archivePath)
    {
        try
        {
            var eintraege = _host.Archives.List(archivePath);
            if (eintraege.Count == 0) return null;

            var haeufigstes = eintraege
                .Select(e => e.Path.Split('/'))
                .Where(teile => teile.Length > 1 && !string.IsNullOrEmpty(teile[0]))
                .GroupBy(teile => teile[0], StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            if (haeufigstes is null) return null;

            // Nur abschneiden, wenn wirklich (fast) alles darunter liegt —
            // sonst wuerde der Filter unten ins Leere greifen.
            return haeufigstes.Count() * 2 >= eintraege.Count ? haeufigstes.Key : null;
        }
        catch (Exception) { return null; }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private static long DirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => { try { return f.Length; } catch { return 0L; } });
        }
        catch (Exception) { return 0; }
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Log.Debug(ex, "Cache-Aufraeumen misslang: {Dir}", dir); }
    }
}
