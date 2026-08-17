using System.Text;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services.Modding;

/// <summary>
/// Schreibt Ren'Py-Translation-Files fuer den KrosteMod-Translation-Mod (E6).
/// Pro Zielsprache eine Datei <c>game/tl/&lt;lang&gt;/krostemod_translations.rpy</c>
/// mit einem <c>translate &lt;lang&gt; strings:</c>-Block der alle uebersetzten
/// Strings als <c>old "..." / new "..."</c>-Paare enthaelt.
///
/// Ren'Py's Translation-System matcht die <c>old</c>-Strings gegen alle Say-
/// und Menu-Texte im Spiel — bei Match wird stattdessen der <c>new</c>-String
/// gerendert. Der User waehlt im Preferences-Menue seine Sprache
/// (Standard-Ren'Py-Feature; Language-Selector erscheint automatisch sobald
/// mind. eine tl/-Sprache existiert).
///
/// **Vorteile gegenueber Body-Rewrite (E4b/c):**
/// - Original bleibt unangetastet — User kann jederzeit zurueckwechseln
/// - Ren'Py-nativ, keine Duplicate-Label-Konflikte
/// - Wenn der Autor spaeter selbst Sprach-Support nachliefert, laesst sich
///   der Mod deinstallieren und die offizielle Uebersetzung uebernimmt.
/// </summary>
public sealed class KrosteTranslationGenerator
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Schreibt fuer jede Zielsprache mit non-leeren Uebersetzungen
    /// eine .rpy-Datei nach <paramref name="destDir"/>. Struktur:
    /// <c>destDir/tl/&lt;renpy-lang-code&gt;/krostemod_translations.rpy</c>.
    /// Zusaetzlich pro Sprache ein <c>krostemod_language_activator_&lt;lang&gt;.rpy</c>
    /// im Game-Root (NICHT im tl-Ordner!) — der aktiviert die Sprache beim
    /// ersten Start und legt einen Overlay-Button an, damit der User
    /// zurueckwechseln kann auch bei Games mit hartcodiertem Language-Cycle
    /// (SteamCity-Bug 2026-08-17: nur ENGLISH/ESPAÑOL/РУССКИЙ im Menu,
    /// German-tl war da, aber unaufrufbar).
    /// Zurueckgegeben werden die geschriebenen Datei-Pfade (relativ zu
    /// <paramref name="destDir"/>) — der Deploy-Loop kopiert sie ins game/.</summary>
    public IReadOnlyList<string> Generate(string destDir, TranslationConfig config)
    {
        Directory.CreateDirectory(destDir);
        var written = new List<string>();
        if (config.TranslatedStrings is null || config.TranslatedStrings.Count == 0)
        {
            Log.Warn("Translation-Config ohne Uebersetzungen — nichts zu generieren");
            return written;
        }

        foreach (var lang in config.TargetLanguages)
        {
            if (!config.TranslatedStrings.TryGetValue(lang, out var strings)
                || strings.Count == 0)
            {
                Log.Info("Sprache {lang}: keine Uebersetzungen, ueberspringe", lang);
                continue;
            }

            string relDir = Path.Combine("tl", lang.ToRenpyCode());
            string absDir = Path.Combine(destDir, relDir);
            Directory.CreateDirectory(absDir);
            string filename = "krostemod_translations.rpy";
            string absPath = Path.Combine(absDir, filename);
            string relPath = Path.Combine(relDir, filename);

            var sb = new StringBuilder();
            WriteHeader(sb, lang, strings.Count);
            WriteStringsBlock(sb, lang, strings);
            File.WriteAllText(absPath, sb.ToString(), new UTF8Encoding(false));
            written.Add(relPath);
            Log.Info("Translation-Datei geschrieben: {path} ({n} Strings)", relPath, strings.Count);

            // v0.16: Language-Activator — MUSS im game/-Root liegen, NICHT
            // in tl/<lang>/, sonst chicken-and-egg (wird erst geladen wenn
            // die Sprache schon aktiv ist, was sie ohne UI-Weg nie wird).
            string activatorName = $"krostemod_language_activator_{lang.ToRenpyCode()}.rpy";
            string activatorAbs = Path.Combine(destDir, activatorName);
            File.WriteAllText(activatorAbs, BuildActivatorRpy(lang),
                new UTF8Encoding(false));
            written.Add(activatorName);
            Log.Info("Language-Activator geschrieben: {path} (Sprache {lang})",
                activatorName, lang.ToRenpyCode());
        }
        return written;
    }

    /// <summary>Baut die Ren'Py-Datei die (a) die Sprache beim ersten Start
    /// automatisch aktiviert und (b) einen Overlay-Button unten rechts
    /// hinzufuegt der jederzeit auf diese Sprache umschaltet.
    ///
    /// <para>Der Overlay-Button ist noetig weil viele Games (bestaetigt bei
    /// SteamCity, oft bei ren'py-Community-Projekten) einen hartcodierten
    /// Language-Cycle im screens.rpy haben der nur die vom Autor
    /// vorgesehenen Sprachen listet. Ren'Py findet den <c>tl/&lt;lang&gt;/</c>-
    /// Ordner zwar, aber ohne UI-Button kann der User ihn nicht aktivieren.</para>
    ///
    /// <para>Der Auto-Activate-Teil laeuft nur EINMAL pro Mod-Version
    /// (persistent-Flag) damit der User seine Wahl behaelt wenn er spaeter
    /// auf Englisch zurueckwechselt.</para></summary>
    private static string BuildActivatorRpy(TargetLanguage lang)
    {
        var code = lang.ToRenpyCode();
        var native = lang.ToNativeName();
        var flag = FlagEmojiFor(lang);
        var sb = new StringBuilder();
        sb.AppendLine("# =====================================================================");
        sb.AppendLine($"# KrosteMod — Language-Activator ({lang.ToPromptName()} / {native})");
        sb.AppendLine("# Automatisch erzeugt von RenPack.");
        sb.AppendLine("#");
        sb.AppendLine("# Aktiviert die Sprache beim ersten Start dieser Mod-Version und legt");
        sb.AppendLine("# einen Overlay-Button unten rechts an — damit User auch bei Games mit");
        sb.AppendLine("# hartcodierten Language-Menus (nur EN/ES/RU o.ae.) auf diese Sprache");
        sb.AppendLine("# umschalten kann.");
        sb.AppendLine("# =====================================================================");
        sb.AppendLine();
        sb.AppendLine("init 999 python:");
        sb.AppendLine($"    _krostemod_lang_target = \"{code}\"");
        sb.AppendLine();
        sb.AppendLine("init 1000 python:");
        sb.AppendLine("    # Beim allerersten Start dieser Mod-Version: Sprache aktiv setzen.");
        sb.AppendLine("    # Danach nicht mehr automatisch ueberschreiben — der User darf");
        sb.AppendLine("    # spaeter frei zwischen Sprachen wechseln (Overlay-Button unten).");
        sb.AppendLine("    _krostemod_flag_attr = \"_krostemod_lang_applied_\" + _krostemod_lang_target");
        sb.AppendLine("    if getattr(persistent, _krostemod_flag_attr, False) is not True:");
        sb.AppendLine("        setattr(persistent, _krostemod_flag_attr, True)");
        sb.AppendLine("        try:");
        sb.AppendLine("            renpy.change_language(_krostemod_lang_target)");
        sb.AppendLine("        except Exception as ex:");
        sb.AppendLine("            renpy.log(\"KrosteMod language activator error: \" + str(ex))");
        sb.AppendLine();
        sb.AppendLine($"screen krostemod_lang_overlay_{code}():");
        sb.AppendLine("    zorder 1500");
        sb.AppendLine("    frame:");
        sb.AppendLine("        xalign 0.995 yalign 0.995");
        sb.AppendLine("        padding (10, 6)");
        sb.AppendLine("        background \"#000000b0\"");
        sb.AppendLine($"        textbutton \"{flag}  {native}\" text_size 18 action Language(\"{code}\")");
        sb.AppendLine();
        sb.AppendLine("init python:");
        sb.AppendLine($"    _krostemod_overlay = \"krostemod_lang_overlay_{code}\"");
        sb.AppendLine("    if _krostemod_overlay not in config.overlay_screens:");
        sb.AppendLine("        config.overlay_screens.append(_krostemod_overlay)");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string FlagEmojiFor(TargetLanguage lang)
    {
        // Native-Flag-Emojis fuer die gaengigen Ren'Py-Sprachcodes.
        // Bei unbekannten Codes: neutraler Globus.
        return lang.ToRenpyCode() switch
        {
            "german" => "🇩🇪",
            "french" => "🇫🇷",
            "spanish" => "🇪🇸",
            "italian" => "🇮🇹",
            "portuguese" => "🇵🇹",
            "russian" => "🇷🇺",
            "polish" => "🇵🇱",
            "dutch" => "🇳🇱",
            "japanese" => "🇯🇵",
            "korean" => "🇰🇷",
            "chinese" => "🇨🇳",
            _ => "🌐",
        };
    }

    private static void WriteHeader(StringBuilder sb, TargetLanguage lang, int count)
    {
        sb.AppendLine("# =====================================================================");
        sb.AppendLine($"# KrosteMod — Translation ({lang.ToPromptName()} / {lang.ToNativeName()})");
        sb.AppendLine($"# Automatisch erzeugt von RenPack. {count} Strings uebersetzt via KI.");
        sb.AppendLine($"# Im Spiel: Preferences → Language → {lang.ToNativeName()}");
        sb.AppendLine("# =====================================================================");
        sb.AppendLine();
    }

    private static void WriteStringsBlock(StringBuilder sb, TargetLanguage lang,
        IReadOnlyDictionary<string, string> strings)
    {
        sb.AppendLine($"translate {lang.ToRenpyCode()} strings:");
        sb.AppendLine();
        foreach (var (original, translated) in strings.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(translated) || translated == original) continue;
            sb.Append("    old \"").Append(EscapeForRenpy(original)).Append("\"").AppendLine();
            sb.Append("    new \"").Append(EscapeForRenpy(translated)).Append("\"").AppendLine();
            sb.AppendLine();
        }
    }

    /// <summary>Escaped einen String fuer die Ren'Py-.rpy-Syntax. Doppelte
    /// Anfuehrungszeichen und Backslashes muessen escaped werden; Escape-
    /// Sequenzen (\n, \t) sind bereits im Input-String literal enthalten
    /// (wir bekommen sie aus dem AST-Text so wie sie in der Original-.rpy
    /// standen).</summary>
    internal static string EscapeForRenpy(string s)
    {
        // Nur echte physische Zeilenumbrueche / Anfuehrungszeichen im String
        // muessen behandelt werden — Ren'Py-Escapes (\n als 2-Zeichen \, n)
        // sind schon Teil des Textes.
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\r': break;             // Ren'Py mag kein CR im String
                case '\n': sb.Append("\\n"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
