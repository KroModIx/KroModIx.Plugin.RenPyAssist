using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Wrapper um <see cref="IAiService"/> für Beschreibungs-Übersetzung
/// in die System-Locale. Cache pro Spiel in
/// <see cref="RenPyGame.DescriptionTranslations"/> — bei zweitem View-Open
/// keine erneute KI-Anfrage.</summary>
public sealed class AiTranslator
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IHostServices _host;

    public AiTranslator(IHostServices host)
    {
        _host = host;
    }

    /// <summary>System-Locale als 2-Buchstaben-Code (z. B. "de", "en", "fr").</summary>
    public static string SystemLocale => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();

    /// <summary>Übersetzt eine Beschreibung in die System-Locale (falls die
    /// System-Locale nicht bereits Englisch ist). Nutzt Cache aus
    /// <see cref="RenPyGame.DescriptionTranslations"/> — bei Cache-Hit sofort
    /// zurück, sonst KI-Call + Cache-Write. Rückgabe: übersetzter Text oder
    /// Original wenn Übersetzung nicht möglich/nötig.</summary>
    public async Task<string> TranslateAsync(RenPyGame game, CancellationToken ct = default)
    {
        var original = game.Description ?? "";
        if (string.IsNullOrWhiteSpace(original)) return "";
        var locale = SystemLocale;
        if (locale == "en") return original;
        if (game.DescriptionTranslations.TryGetValue(locale, out var cached)
            && !string.IsNullOrEmpty(cached))
            return cached;

        // KI verfügbar?
        try
        {
            if (!await _host.Ai.IsAvailableAsync(ct)) return original;
        }
        catch { return original; }

        try
        {
            var localeName = locale switch
            {
                "de" => "Deutsch",
                "fr" => "Französisch",
                "es" => "Spanisch",
                "it" => "Italienisch",
                "pt" => "Portugiesisch",
                "ru" => "Russisch",
                _ => new CultureInfo(locale).DisplayName,
            };
            var system = $"Du bist ein Übersetzer. Übersetze den vom User gegebenen englischen " +
                $"Spiel-Beschreibungstext nach {localeName}. Antworte NUR mit der Übersetzung, " +
                $"ohne Einleitung, Kommentar oder Formatierung. Behalte Zeilenumbrüche bei.";
            var translated = await _host.Ai.CompleteAsync(system, original, ct);
            translated = translated?.Trim() ?? "";
            if (string.IsNullOrEmpty(translated)) return original;
            game.DescriptionTranslations[locale] = translated;
            return translated;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "KI-Übersetzung fehlgeschlagen — Original wird angezeigt");
            return original;
        }
    }
}
