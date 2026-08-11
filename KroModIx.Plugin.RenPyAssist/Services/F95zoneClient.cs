using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>HTTP-Client für f95zone.to: Login mit CSRF-Handshake, Session-
/// Cookie-Verwaltung, Search-Endpoint und Thread-Metadata-Scrape.
///
/// <para><b>Robustheit:</b> f95zone hat kein öffentliches API — HTML wird
/// per Regex geparst (kein HtmlAgilityPack, um die Plugin-DLL schlank zu
/// halten). Bei Layout-Änderungen der Seite muss der Regex angepasst
/// werden — kein Grund für Crashes im Host, alle Methoden geben bei Fehler
/// ein sinnvolles Default zurück (leere Liste / null).</para>
///
/// <para><b>Cookies</b> werden im <see cref="CookieContainer"/> gehalten,
/// exportier-/importierbar über <see cref="ExportCookies"/> /
/// <see cref="ImportCookies"/> — persistent verschlüsselt über
/// <c>ISecretProtection</c> (Host-Service).</para></summary>
public sealed class F95zoneClient : IDisposable
{
    internal const string BaseUrl = "https://f95zone.to";
    // f95zone erwartet einen realistischen User-Agent — mit einem Custom-UA
    // landet man häufig im Cloudflare-Challenge.
    private const string UserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly HttpClientHandler _handler;
    private readonly CookieContainer _cookies;

    public F95zoneClient()
    {
        _cookies = new CookieContainer();
        _handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(_handler) { BaseAddress = new Uri(BaseUrl) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>Meldet den User mit Email/Passwort an. Rückgabe true =
    /// Login erfolgreich (xf_user-Cookie gesetzt). Bei Fehler wird
    /// <see cref="F95zoneAuthException"/> geworfen.</summary>
    public async Task<bool> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        using var pageResponse = await _http.GetAsync("/login/", ct);
        pageResponse.EnsureSuccessStatusCode();
        string page = await pageResponse.Content.ReadAsStringAsync(ct);
        string? xfToken = ExtractXfToken(page);
        if (xfToken is null)
            throw new F95zoneAuthException("Kein _xfToken auf der Login-Seite gefunden — Seiten-Layout hat sich geändert oder Cloudflare-Challenge.");

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("login", email),
            new KeyValuePair<string, string>("password", password),
            new KeyValuePair<string, string>("remember", "1"),
            new KeyValuePair<string, string>("_xfToken", xfToken),
            new KeyValuePair<string, string>("_xfRedirect", BaseUrl + "/"),
        });
        using var loginResponse = await _http.PostAsync("/login/login", form, ct);
        loginResponse.EnsureSuccessStatusCode();

        return _cookies.GetCookies(new Uri(BaseUrl))["xf_user"] is not null;
    }

    /// <summary>Ist der aktuelle Cookie-Container eingeloggt? Prüft lokal
    /// ob ein xf_user-Cookie existiert — keine Netzwerk-Anfrage.</summary>
    public bool IsAuthenticated =>
        _cookies.GetCookies(new Uri(BaseUrl))["xf_user"] is not null;

    /// <summary>Sucht Threads auf f95zone. Rückgabe: Top-Ergebnisse
    /// sortiert nach Relevanz (wie F95zones eigene Search-UI).</summary>
    public async Task<IReadOnlyList<F95Thread>> SearchAsync(string query, CancellationToken ct = default)
    {
        var escaped = Uri.EscapeDataString(query);
        using var resp = await _http.GetAsync($"/quicksearch/?q={escaped}&t=post", ct);
        if (!resp.IsSuccessStatusCode) return Array.Empty<F95Thread>();
        string html = await resp.Content.ReadAsStringAsync(ct);
        return ExtractSearchResults(html);
    }

    /// <summary>Lädt einen einzelnen Thread und extrahiert Titel, Versions-
    /// String, Cover-Bild-URL, Beschreibung und Genre-Tags. Titel/Version aus
    /// <c>og:title</c>, Cover aus dem ersten <c>attachments.f95zone.*</c>-
    /// Full-Size-Bild, Beschreibung aus dem ersten Post-Body (Overview-
    /// Sektion), Genre aus den Prefix-Tags im Titel-Header.</summary>
    public async Task<F95ThreadInfo?> FetchThreadInfoAsync(string threadUrl, CancellationToken ct = default)
    {
        Uri url = new(threadUrl);
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        string html = await resp.Content.ReadAsStringAsync(ct);
        var title = ExtractThreadTitle(html);
        if (title is null) return null;
        var version = ExtractVersion(title);
        var coverUrl = ExtractCoverUrl(html);
        var description = ExtractDescription(html);
        var genres = ExtractGenres(html);
        return new F95ThreadInfo(threadUrl, title, version, coverUrl, description, genres);
    }

    /// <summary>Cover-Image herunterladen. Nutzt die Session-Cookies
    /// (F95zone-Attachments brauchen manchmal Login). Byte-Array oder
    /// null bei Fehler.</summary>
    public async Task<byte[]?> DownloadImageAsync(string imageUrl, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(imageUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch { return null; }
    }

    /// <summary>Cookie-Container als Blob exportieren. Nur f95zone-relevante
    /// Cookies (xf_session, xf_user, xf_csrf, xf_tfa_trust).</summary>
    public string ExportCookies()
    {
        var relevant = new[] { "xf_session", "xf_user", "xf_csrf", "xf_tfa_trust" };
        var lines = new List<string>();
        foreach (Cookie c in _cookies.GetCookies(new Uri(BaseUrl)))
        {
            if (!relevant.Contains(c.Name)) continue;
            long expires = c.Expires == DateTime.MinValue
                ? 0 : new DateTimeOffset(c.Expires.ToUniversalTime()).ToUnixTimeSeconds();
            lines.Add($"{c.Name}\t{c.Value}\t{c.Domain}\t{c.Path}\t{expires}");
        }
        return string.Join("\n", lines);
    }

    public void ImportCookies(string blob)
    {
        if (string.IsNullOrWhiteSpace(blob)) return;
        foreach (var line in blob.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 4) continue;
            var cookie = new Cookie(parts[0], parts[1], parts[3], parts[2]);
            if (parts.Length >= 5 && long.TryParse(parts[4], out long expiresUnix) && expiresUnix > 0)
                cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(expiresUnix).UtcDateTime;
            _cookies.Add(cookie);
        }
    }

    // ---- HTML-Parser ------------------------------------------------------

    private static readonly Regex XfTokenPattern = new(
        @"name=""_xfToken""\s+value=""([^""]+)""",
        RegexOptions.Compiled);
    private static string? ExtractXfToken(string html)
        => XfTokenPattern.Match(html) is { Success: true } m ? m.Groups[1].Value : null;

    private static readonly Regex TitlePattern = new(
        @"<meta\s+property=""og:title""\s+content=""([^""]+)""",
        RegexOptions.Compiled);
    private static string? ExtractThreadTitle(string html)
    {
        var m = TitlePattern.Match(html);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    // F95zone setzt og:image auf ihr Favicon (nutzlos). Echte Cover liegen
    // als Attachments: https://attachments.f95zone.to/YYYY/MM/ID_filename.ext
    // Thumbnails via .../thumb/... — wir wollen das erste Full-Size.
    private static readonly Regex AttachmentPattern = new(
        @"https://attachments\.f95zone\.[a-z]+/\d{4}/\d{2}/(?!thumb/)[^""'\s]+\.(?:jpg|jpeg|png|gif|webp)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static string? ExtractCoverUrl(string html)
    {
        var m = AttachmentPattern.Match(html);
        return m.Success ? m.Value : null;
    }

    /// <summary>Extrahiert die Versions-Nummer aus einem f95zone-Thread-
    /// Titel. Ren'Py-Games taggen ihre Threads typischerweise als
    /// <c>Game Name [v0.15.0] [Studio] [Genre]</c>. Zweitform ohne eckige
    /// Klammern (<c>Game Name v0.15.0</c>) fängt Regex auch.</summary>
    public static string? ExtractVersion(string text)
    {
        var bracket = Regex.Match(text, @"\[\s*v?\s*(\d+(?:\.\d+){0,3}[a-zA-Z0-9]*)\s*\]");
        if (bracket.Success) return bracket.Groups[1].Value;
        var loose = Regex.Match(text, @"\bv\s*(\d+(?:\.\d+){1,3}[a-zA-Z0-9]*)\b",
            RegexOptions.IgnoreCase);
        return loose.Success ? loose.Groups[1].Value : null;
    }

    // Der erste Post im Thread — meist mit "Overview:" oder direkt beschreibend.
    // Wir suchen einen div.bbWrapper (XenForo-Standard-Post-Body).
    private static readonly Regex FirstPostBodyPattern = new(
        @"<div\s+class=""bbWrapper"">(.*?)</div>\s*(?=<div\s+class=""message-content"" |<article|<footer|$)",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    // "Overview:"-Block extrahieren wenn präsent, sonst gesamten Post.
    private static readonly Regex OverviewSectionPattern = new(
        @"(?:<b>|<strong>)?\s*Overview\s*:?\s*(?:</b>|</strong>)?\s*<br\s*/?>\s*(.*?)(?=<b>|<strong>|<hr|<a\s+href|<img|<div\s+class=""bbCodeBlock)",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static string? ExtractDescription(string html)
    {
        var body = FirstPostBodyPattern.Match(html);
        if (!body.Success) return null;
        var bodyHtml = body.Groups[1].Value;
        var overview = OverviewSectionPattern.Match(bodyHtml);
        string text = overview.Success ? overview.Groups[1].Value : bodyHtml;
        text = StripHtml(text);
        text = text.Trim();
        if (text.Length > 2000) text = text[..2000] + "…";
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Genre-Tags aus dem Thread-Header. F95zone rendert sie in einem
    /// <c>bbCodeSpoiler</c>-Container nach dem Titel; als Fallback nutzen wir
    /// die <c>meta name="keywords"</c>-Zeile.</summary>
    private static readonly Regex GenreSpoilerPattern = new(
        @"<div\s+class=""bbCodeSpoiler-content[^""]*"">\s*<div[^>]*>\s*([^<]{20,500})\s*</div>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex KeywordsMetaPattern = new(
        @"<meta\s+name=""keywords""\s+content=""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static IReadOnlyList<string> ExtractGenres(string html)
    {
        // f95zone Genre-Sektion enthält typischerweise ein "Genre:"-Label
        // gefolgt von einem Spoiler-Block mit Komma-getrennten Tags.
        // Sample: "3DCG, Big ass, Big tits, Corruption, ..."
        int genreIdx = html.IndexOf("Genre", StringComparison.OrdinalIgnoreCase);
        if (genreIdx > 0)
        {
            var slice = html.Substring(genreIdx, Math.Min(3000, html.Length - genreIdx));
            var m = GenreSpoilerPattern.Match(slice);
            if (m.Success)
            {
                return SplitTags(WebUtility.HtmlDecode(m.Groups[1].Value));
            }
        }
        // Fallback: Meta-Keywords
        var kw = KeywordsMetaPattern.Match(html);
        if (kw.Success)
        {
            return SplitTags(WebUtility.HtmlDecode(kw.Groups[1].Value))
                .Take(20).ToList();
        }
        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> SplitTags(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Where(t => t.Length is > 1 and < 40)
           .Distinct(StringComparer.OrdinalIgnoreCase)
           .ToList();

    // XenForo-BBCode-HTML → Plaintext. Behält Zeilenumbrüche bei <br> und </p>.
    private static readonly Regex HtmlTagPattern = new(@"<[^>]+>", RegexOptions.Compiled);
    private static string StripHtml(string html)
    {
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</p>", "\n\n", RegexOptions.IgnoreCase);
        html = HtmlTagPattern.Replace(html, "");
        html = WebUtility.HtmlDecode(html);
        html = Regex.Replace(html, @"\n{3,}", "\n\n");
        return html;
    }

    private static readonly Regex QuicksearchThreadPattern = new(
        @"<a\s+[^>]*href=""(/threads/[^""]+)""[^>]*>\s*<span[^>]*>([^<]+)</span>",
        RegexOptions.Compiled);
    private static IReadOnlyList<F95Thread> ExtractSearchResults(string html)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<F95Thread>();
        foreach (Match m in QuicksearchThreadPattern.Matches(html))
        {
            string href = m.Groups[1].Value;
            if (!seen.Add(href)) continue;
            string title = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            results.Add(new F95Thread(
                Url: BaseUrl + href,
                Title: title,
                Version: ExtractVersion(title)));
            if (results.Count >= 10) break;
        }
        return results;
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }
}

public sealed record F95Thread(string Url, string Title, string? Version);

public sealed record F95ThreadInfo(
    string Url,
    string Title,
    string? Version,
    string? CoverUrl,
    string? Description,
    IReadOnlyList<string> Genres);

public sealed class F95zoneAuthException : Exception
{
    public F95zoneAuthException(string message) : base(message) { }
}
