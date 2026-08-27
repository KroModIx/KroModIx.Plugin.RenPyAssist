using System.Collections.Generic;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Uebersetzungs-Tabelle fuer alle User-facing Strings im
/// Ren'Py-Assist-Plugin. Sprachen: <c>de</c> (Fallback) + <c>en</c>.
///
/// <para>Nutzung: <c>Strings.Init(host.Localization)</c> beim Plugin-Init,
/// dann ueberall <c>Strings.T("key")</c>. Bei fehlendem Key wird der Key
/// selbst zurueckgegeben (macht Missing-Translations sofort sichtbar).</para>
///
/// <para><b>Kein Live-Refresh bei Sprachwechsel:</b> die Strings werden
/// zum View-Constructor-Zeitpunkt gelesen. Bei Sprachwechsel im Host muss
/// der User die Ren'Py-Kachel neu waehlen (Host-Tab-Cache erzeugt dann
/// neue View-Instanzen mit den frischen Uebersetzungen) oder die App neu
/// starten. Vollreactive-Bindings waeren komplex und lohnen sich fuer den
/// seltenen Anwendungsfall nicht.</para></summary>
public static class Strings
{
    private static ILocalization? _loc;

    public static void Init(ILocalization loc) => _loc = loc;

    public static string T(string key)
    {
        var iso = _loc?.CurrentIso ?? "de";
        if (iso.StartsWith("en") && En.TryGetValue(key, out var en)) return en;
        if (De.TryGetValue(key, out var de)) return de;
        return key;
    }

    private static readonly Dictionary<string, string> De = new()
    {
        // Tab-Labels
        ["tab.overview"] = "Übersicht",
        ["tab.archives"] = "Archive",
        ["tab.saves"] = "Saves",
        ["tab.mods"] = "Mods",
        ["tab.settings"] = "Einstellungen",

        // --- Common buttons ---
        ["btn.refresh"] = "🔄  Aktualisieren",
        ["btn.refresh_short"] = "🔄  Refresh",
        ["btn.rescan"] = "🔄  Neu scannen",
        ["btn.check"] = "🔄  Prüfen",
        ["btn.save"] = "💾  Speichern",
        ["btn.save_changes"] = "💾  Änderungen speichern",
        ["btn.cancel"] = "Abbrechen",
        ["btn.close"] = "Schließen",
        ["btn.ok"] = "OK",
        ["btn.apply"] = "✓  Übernehmen",
        ["btn.rename"] = "Umbenennen",
        ["btn.extract_selected"] = "⬇  Datei entpacken",
        ["btn.extract_all"] = "⬇⬇  Alles entpacken",
        ["btn.play_inline"] = "▶  Inline abspielen",
        ["btn.pause"] = "⏸  Pause",
        ["btn.open_external"] = "⤴  Extern öffnen",
        ["btn.pick"] = "📂  Wählen",
        ["btn.open_folder"] = "📂  Ordner",
        ["btn.open_saves_folder"] = "📂  saves/-Ordner",
        ["btn.start"] = "▶  Start",
        ["btn.install_update"] = "⬆  Update installieren",
        ["btn.rename_folder"] = "✏  Ordner umbenennen",
        ["btn.choose_sidebar_crop"] = "🖼  Sidebar-Ausschnitt wählen",
        ["btn.save_thread"] = "💾  Thread speichern & prüfen",
        ["btn.open_thread"] = "🔗  Thread öffnen",
        ["btn.save_global"] = "💾  Global-Einstellungen speichern",
        ["btn.login"] = "🔐  Einloggen",
        ["btn.logout"] = "🚪  Cookies löschen",
        ["btn.open_f95"] = "↗  f95zone.to öffnen",
        ["btn.build"] = "▶  Bauen",
        ["btn.uninstall"] = "🗑  Deinstallieren",
        ["btn.decompile_rpyc"] = "🔓  .rpyc dekompilieren",
        ["btn.crop_save"] = "💾  Ausschnitt speichern",
        ["btn.go"] = "▶  Los",

        // --- Placeholders ---
        ["placeholder.thread_url"] = "https://f95zone.to/threads/…",
        ["placeholder.username"] = "Username / Email",
        ["placeholder.password"] = "Passwort",
        ["placeholder.saves_filter"] = "Variable filtern (z. B. money, love, points) …",
        ["placeholder.new_name"] = "(unverändert)",

        // --- Tooltips ---
        ["tooltip.rename_folder"] =
            "Benennt den Container-Ordner auf der Platte um. .renpyassist/-Metadaten " +
            "wandern mit. Sidebar-Kachel und Detail-View werden re-keyed.",
        ["tooltip.choose_crop"] =
            "Öffnet einen Dialog mit dem Original-Cover — verschiebe den 2:3-Rahmen " +
            "und speichere. Der Ausschnitt landet als Sidebar-Kachel.",
        ["tooltip.open_thread"] =
            "Öffnet den verknüpften f95zone-Thread im Standard-Browser. "
            + "Ohne verknüpften Thread inaktiv — Link in den Einstellungen (⚙) eintragen.",
        ["tooltip.inline_play"] = "MJPEG-Frame-Stream via ffmpeg (kein Audio, 12 fps)",
        ["tooltip.inline_stop"] = "Inline-Wiedergabe stoppen",
        ["tooltip.open_external"] = "Im System-Default-Player öffnen (VLC/mpv/…)",
        ["tooltip.decompile_rpyc"] =
            "Dekompiliert alle .rpyc-Dateien im aktiven game/-Ordner nach .rpy " +
            "(portiert aus RenPack). Bereits vorhandene, aktuelle .rpy werden übersprungen.",

        // --- Section labels / headers ---
        ["section.thread_url"] = "f95zone-Thread-URL",
        ["section.thread_help"] =
            "Link zum f95zone-Thread. Wenn gesetzt: Version-Checks, Cover, " +
            "Beschreibung und Genre werden automatisch geladen.",
        ["section.actions"] = "Aktionen",
        ["section.plugin_global_header"] = "Plugin-Einstellungen (global)",
        ["section.downloads_dir"] = "Downloads-Watch-Ordner",
        ["section.interval"] = "Update-Check-Intervall (Minuten)",
        ["section.f95_login"] = "f95zone-Login",
        ["section.f95_login_help"] =
            "Login ist optional aber empfohlen für Cover-Downloads. Passwort wird " +
            "NIE gespeichert — nur Session-Cookies verschlüsselt via Host-Secrets.",
        ["section.description"] = "Beschreibung",
        ["section.genre"] = "Genre",
        ["section.mod_type"] = "Mod-Typ wählen",
        ["section.krostemod_title"] = "KrosteMod-Pipeline",
        ["section.krostemod_subtitle"] =
            "Portiert aus RenPack (Kroste-Original). Wählt einen Typ, klick 'Bauen' — " +
            "das Plugin dekompiliert alle .rpyc, analysiert die Skripte, generiert den " +
            "Mod und deployt ihn ins game/-Verzeichnis. Original-.rpyc werden als " +
            ".krostemod-bak gesichert; Uninstall stellt sie wieder her.",

        // --- Status messages ---
        ["status.game_dir_missing"] = "game/-Ordner nicht gefunden: {0}",
        ["status.saves_dir_missing"] = "saves/-Ordner nicht gefunden: {0}",
        ["status.rpa_scan_result"] = "{0} .rpa-Archiv(e) gefunden",
        ["status.loading_index"] = "Lade Index: {0} …",
        ["status.index_summary"] = "{0} · {1} Datei(en) · {2}",
        ["status.index_error"] = "Index-Fehler: {0}",
        ["status.preview_too_large"] = " · zu groß für Preview",
        ["status.preview_binary"] = " · binär (kein Preview)",
        ["status.preview_error"] = " · Fehler: {0}",
        ["status.preview_image_decode_fail"] = " · Bild-Decode fehlgeschlagen",
        ["status.saves_count"] = "{0} Save(s) im Ordner {1}",
        ["status.loading_save"] = "Lade Save: {0} …",
        ["status.vars_editable"] = "{0} Variable(n) editierbar",
        ["status.log_error"] = "Log-Fehler: {0}",
        ["status.load_error"] = "Lade-Fehler: {0}",
        ["status.mods_default"] = "Wähle einen Mod-Typ und klick 'Bauen'.",
        ["status.building"] = "Baue {0} …",
        ["status.mod_deployed"] = "✔ Mod deployed: {0} Datei(en) in {1}",
        ["status.error_prefix"] = "Fehler: {0}",
        ["status.searching_rpyc"] = "Suche .rpyc-Dateien …",
        ["status.decompile_progress"] = "Dekompiliere {0}/{1} …",
        ["status.decompile_ok"] = "✔ {0} dekompiliert, {1} übersprungen (bereits aktuell).",
        ["status.decompile_partial"] = "⚠ {0} OK, {1} Fehler, {2} übersprungen — Log prüfen.",
        ["status.krostemod_none"] = "Kein KrosteMod aktiv.",
        ["status.krostemod_installed"] = "KrosteMod installiert (Manifest: {0})",
        ["status.uninstalling"] = "Deinstalliere …",
        ["status.uninstall_ok"] = "✔ {0} Datei(en) entfernt, {1} Backup(s) restauriert.",
        ["status.no_krostemod"] = "Kein KrosteMod installiert — nichts zu deinstallieren.",
        ["status.no_thread_check"] = "Kein Thread-URL — nichts zu prüfen.",
        ["status.checking_thread"] = "Prüfe Thread …",
        ["status.check_done"] = "Check fertig.",
        ["status.check_error"] = "Check-Fehler: {0}",
        ["status.unpacking"] = "Entpacke …",
        ["status.update_done"] = "Update fertig.",
        ["status.status_error_prefix"] = "Fehler: {0}",
        ["status.link_removed"] = "Verknüpfung entfernt.",
        ["status.thread_saved"] = "Thread gespeichert — prüfe jetzt …",
        ["status.renaming"] = "Benenne Ordner um …",
        ["status.renamed"] = "Ordner umbenannt.",
        ["status.rename_fail"] = "Rename fehlgeschlagen.",
        ["status.global_saved"] = "Gespeichert um {0}.",
        ["status.login_logging_in"] = "Logging in …",
        ["status.login_missing_creds"] = "⚠ Bitte Username + Passwort eintragen.",
        ["status.login_ok"] = "✔ Eingeloggt als {0}",
        ["status.login_fail"] = "✘ Login fehlgeschlagen (falsche Credentials?)",
        ["status.login_error"] = "✘ Fehler: {0}",
        ["status.login_prefix_fail"] = "✘ {0}",
        ["status.logout_done"] = "✘ Nicht eingeloggt (Cookies gelöscht — Plugin-Restart nötig)",
        ["status.login_logged_in_as"] = "✔ Eingeloggt als {0}",
        ["status.login_logged_in_unknown"] = "(unbekannt)",
        ["status.login_not_logged_in"] = "✘ Nicht eingeloggt",
        ["status.container_prefix"] = "Container: {0}",
        ["status.last_checked_prefix"] = "zuletzt geprüft: {0}",
        ["status.no_thread_hint"] =
            "Kein f95zone-Thread verknüpft. In den Einstellungen (⚙) rechts oben " +
            "kannst du den Thread-Link eintragen — dann erscheinen Cover, " +
            "Beschreibung, Genre und Update-Checks automatisch.",
        ["status.desc_will_load"] =
            "(Beschreibung wird beim nächsten Thread-Check geladen — 🔄 Prüfen in den Einstellungen klicken.)",
        ["status.desc_translating"] = "(Original — KI übersetzt gerade …)",
        ["status.desc_translated_cached"] = "(via KI übersetzt, Cache — Original s. Einstellungen)",
        ["status.desc_translated"] = "(via KI übersetzt)",
        ["status.desc_translation_unavailable"] = "(KI nicht verfügbar oder Übersetzung leer — Original angezeigt)",
        ["status.subpath_prefix"] = "Sub-Path: {0}",
        ["status.version_line"] = "lokal: {0}  ·  remote: {1}",
        ["status.update_badge"] = "↑ Update",
        ["status.update_badge_with_version"] = "↑ {0}",

        // --- Notifications ---
        ["notify.new_zip"] = "Neue Ren'Py-ZIP: {0}",
        ["notify.extern_opened"] = "Extern geöffnet: {0}",
        ["notify.extracted"] = "Entpackt: {0}",
        ["notify.extracted_count"] = "{0} Datei(en) aus {1} entpackt",
        ["notify.saves_no_changes"] = "Keine Änderungen zum Speichern",
        ["notify.saves_changes_saved"] = "{0} Änderung(en) gespeichert",
        ["notify.thread_linked"] = "Thread verknüpft: {0}",
        ["notify.update_installed"] = "Update installiert: {0} → {1}",
        ["notify.settings_saved"] = "Einstellungen gespeichert",
        ["notify.f95_login_ok"] = "f95zone-Login erfolgreich",
        ["notify.f95_cookies_cleared"] = "f95zone-Cookies gelöscht",
        ["notify.folder_renamed"] = "Ordner umbenannt: „{0}\" → „{1}\"",
        ["notify.folder_renamed_pending"] = "Ordner umbenannt (Host-Kachel wird beim Neustart nachgezogen)",
        ["notify.sidebar_crop_saved"] = "Sidebar-Ausschnitt gespeichert und gesetzt.",
        ["notify.mod_installed"] = "{0} für {1} installiert ({2} Datei(en))",
        ["notify.krostemod_uninstalled"] = "KrosteMod deinstalliert",
        ["notify.decompile_summary"] = "Decompile: {0}/{1} .rpyc → .rpy",
        ["notify.decompile_error"] = "Decompile-Fehler: {0}",

        // --- Dialogs ---
        ["dialog.confirm"] = "Bestätigen",
        ["dialog.error"] = "Fehler",
        ["dialog.remove_link_title"] = "Verknüpfung entfernen",
        ["dialog.remove_link_msg"] = "Thread-URL für „{0}\" entfernen?",
        ["dialog.invalid_url_title"] = "URL ungültig",
        ["dialog.invalid_url_msg"] = "Bitte eine vollständige http(s)://-URL eintragen.",
        ["dialog.install_update_title"] = "Update installieren",
        ["dialog.install_update_msg"] =
            "ZIP wird in „{0}\" entpackt. Save-Games werden aus dem alten " +
            "Sub-Ordner in den neuen kopiert. Der alte Sub-Ordner wird anschließend " +
            "gelöscht. Die ZIP-Datei wird in „archive/\" archiviert.\n\nFortfahren?",
        ["dialog.install_fail_title"] = "Install fehlgeschlagen",
        ["dialog.install_fail_unknown"] = "Unbekannter Fehler.",
        ["dialog.no_cover_title"] = "Kein Cover",
        ["dialog.no_cover_msg"] =
            "Es gibt noch kein Cover-Bild zum Zuschneiden. Erst einen f95zone-" +
            "Thread eintragen und 🔄 Prüfen klicken.",
        ["dialog.no_launcher_title"] = "Kein Launcher",
        ["dialog.no_launcher_msg"] = "Kein .sh/.exe im aktiven Sub-Ordner gefunden:\n{0}",
        ["dialog.rename_title"] = "Ordner umbenennen",
        ["dialog.rename_missing_msg"] = "Container-Ordner existiert nicht:\n{0}",
        ["dialog.rename_prompt"] =
            "Neuer Ordnername für „{0}\":\n(Der Container-Ordner auf der Platte wird umbenannt. " +
            "Container-lokale Metadaten in .renpyassist/ wandern mit.)",
        ["dialog.rename_invalid_chars"] =
            "Der Name enthält ungültige Zeichen (/, \\, :, *, ?, \", <, >, |).",
        ["dialog.rename_target_exists"] = "Zielpfad existiert bereits:\n{0}",
        ["dialog.rename_fail_msg"] = "Umbenennen fehlgeschlagen:\n{0}",
        ["dialog.pick_downloads"] = "Downloads-Ordner wählen",
        ["dialog.pick_folder_extract"] = "Zielordner für Extraktion",
        ["dialog.pick_folder_extract_all"] = "Zielordner für gesamtes Archiv",
        ["dialog.pick_update_zip"] = "Update-ZIP für „{0}\"",
        ["dialog.zip_filter"] = "Ren'Py-ZIPs",
        ["dialog.extract_all_title"] = "Alles entpacken?",
        ["dialog.extract_all_msg"] = "{0} Datei(en) werden nach\n{1}\nentpackt. Fortfahren?",
        ["dialog.extract_error_title"] = "Extract-Fehler",
        ["dialog.save_overwrite_title"] = "Save überschreiben?",
        ["dialog.save_overwrite_msg"] =
            "{0} Variable(n) werden im Save „{1}\" gepatched.\n" +
            "Ren'Py-Saves werden byte-preserving editiert — Roundtrip-safe. Trotzdem: " +
            "vorher Backup empfohlen.\n\nFortfahren?",
        ["dialog.save_error_title"] = "Save-Fehler",
        ["dialog.value_invalid_title"] = "Wert ungültig",
        ["dialog.value_invalid_msg"] = "„{0}\": „{1}\" ist kein gültiges Python-Literal.",
        ["dialog.build_confirm_title"] = "{0}-Mod bauen?",
        ["dialog.build_confirm_msg"] =
            "Der Mod wird für „{0}\" gebaut und in " +
            "„{1}\" deployt. Alle originalen .rpyc werden als .krostemod-bak " +
            "gesichert. Deinstallation über „🗑 Deinstallieren\" — restauriert Originale.\n\n" +
            "Fortfahren?",
        ["dialog.mods_game_dir_title"] = "game/-Ordner nicht gefunden",
        ["dialog.mods_game_dir_msg"] = "Erwartet: {0}",
        ["dialog.krostemod_uninstall_title"] = "KrosteMod deinstallieren?",
        ["dialog.krostemod_uninstall_msg"] =
            "Alle modifizierten .rpy werden gelöscht, .rpyc-Backups (.krostemod-bak) werden " +
            "restauriert.\n\nFortfahren?",

        // --- Cover-Crop-Dialog ---
        ["crop.title"] = "Sidebar-Kachel-Ausschnitt wählen",
        ["crop.info"] =
            "Verschiebe den goldenen Rahmen mit der Maus, Größe per Slider. " +
            "Zielformat 2:3 (Steam-Library-Portrait, 600×900).",
        ["crop.zoom_label"] = "Zoom",
        ["crop.status_load_fail"] = "Bild-Load fehlgeschlagen: {0}",
        ["crop.status_save_fail"] = "Save-Fehler: {0}",
        ["crop.status_summary"] = "Ausschnitt: {0}×{1} px  (Position {2},{3} · Original {4}×{5})",

        // --- Rename-Dialog ---
        ["rename.title"] = "Character umbenennen",
        ["rename.header"] = "{0} Character im Spiel erkannt",
        ["rename.help"] =
            "Trage neue Namen in die rechte Spalte ein. Leerer Text = keine Änderung. " +
            "Nach Übernehmen: Plugin schreibt Character-Objekt-Namen um und (falls Ollama/" +
            "Cloud konfiguriert) lässt die KI alle Body-Texte konsistent umschreiben " +
            "(Grammatik, Beziehungswörter).",

        // --- Translate-Setup-Dialog ---
        ["translate.title"] = "Übersetzung einrichten",
        ["translate.header"] = "🌐  Ren'Py-Spiel übersetzen",
        ["translate.stats"] =
            "{0} Dialog-Zeilen im Spiel · {1} eindeutige Texte " +
            "(nach Dedup) → wird via KI-Batch übersetzt (30 Texte/Batch).",
        ["translate.time_estimate"] =
            "Zeit-Schätzung: Ollama ~5-10 s/Batch, Cloud ~2-3 s/Batch. " +
            "Bei 500 Says ≈ 20 Batches → 1-3 min.",
        ["translate.target_lang"] = "Zielsprache",
        ["translate.status"] = "🌐 KI-Übersetzung ({0}) läuft …",
        ["translate.progress_label"] = "KI-Übersetzung {0}",
        ["translate.fail"] = "KI-Übersetzung fehlgeschlagen: {0}",

        // --- Mod-Types (auf ModTypeOption) ---
        ["mod.walkthrough.name"] = "Walkthrough",
        ["mod.walkthrough.desc"] =
            "Zeigt in Choice-Menus die besten Optionen — Variablen-basiert per Regex-Analyse.",
        ["mod.cheat.name"] = "Cheat",
        ["mod.cheat.desc"] =
            "F11-Overlay im Spiel: alle Store-Variablen live editieren (Geld, Beziehungswerte, Flags).",
        ["mod.rename.name"] = "Rename",
        ["mod.rename.desc"] =
            "Character-Umbenennung mit Editor-Dialog (Alt→Neu). Wenn KI konfiguriert: " +
            "Body-Texte werden konsistent umgeschrieben (Grammatik, Beziehungswörter).",
        ["mod.translate.name"] = "Translate",
        ["mod.translate.desc"] =
            "KI-Batch-Übersetzung aller Dialoge in eine Zielsprache. Braucht Host-KI " +
            "(Ollama/Cloud). Ollama: ~5-10 s/Batch, Cloud: ~2-3 s/Batch. Bei 500 Says ≈ 1-3 min.",

        // --- Archive-Row ---
        ["archive.summary"] = "{0}  ·  {1}  ·  {2} Files",

        // --- Saves metadata labels ---
        ["saves.meta.slot"] = "Slot: {0}",
        ["saves.meta.time"] = "Zeit: {0}",
        ["saves.meta.game"] = "Spiel: {0}",
        ["saves.meta.renpy"] = "Ren'Py: {0}",

        // --- ffmpeg-Missing-Hint ---
        ["hint.ffmpeg_missing"] =
            "ffmpeg fehlt — für Inline-Playback bitte installieren:\n" +
            "Fedora/Bazzite: sudo dnf install ffmpeg-free\n" +
            "Debian/Ubuntu: sudo apt install ffmpeg\n" +
            "Windows: winget install ffmpeg\n\n" +
            "Der externe Player funktioniert trotzdem.",

        // --- Launcher/Update notify (RenPyAssistPlugin) ---
        ["notify.game_started"] = "Ren'Py-Spiel gestartet: {0}",
        ["notify.game_start_fail"] = "Start fehlgeschlagen: {0}",
        ["notify.no_launcher"] = "Kein Ren'Py-Launcher in „{0}\" gefunden.",
        ["notify.update_thread_opened"] = "Update {0} für „{1}\" — Thread im Browser geöffnet.",
        ["notify.update_available_summary"] = "Ren'Py-Update verfügbar: {0}",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        // Tab-Labels
        ["tab.overview"] = "Overview",
        ["tab.archives"] = "Archives",
        ["tab.saves"] = "Saves",
        ["tab.mods"] = "Mods",
        ["tab.settings"] = "Settings",

        // Common buttons
        ["btn.refresh"] = "🔄  Refresh",
        ["btn.refresh_short"] = "🔄  Refresh",
        ["btn.rescan"] = "🔄  Rescan",
        ["btn.check"] = "🔄  Check",
        ["btn.save"] = "💾  Save",
        ["btn.save_changes"] = "💾  Save changes",
        ["btn.cancel"] = "Cancel",
        ["btn.close"] = "Close",
        ["btn.ok"] = "OK",
        ["btn.apply"] = "✓  Apply",
        ["btn.rename"] = "Rename",
        ["btn.extract_selected"] = "⬇  Extract file",
        ["btn.extract_all"] = "⬇⬇  Extract all",
        ["btn.play_inline"] = "▶  Play inline",
        ["btn.pause"] = "⏸  Pause",
        ["btn.open_external"] = "⤴  Open external",
        ["btn.pick"] = "📂  Pick",
        ["btn.open_folder"] = "📂  Folder",
        ["btn.open_saves_folder"] = "📂  saves/ folder",
        ["btn.start"] = "▶  Start",
        ["btn.install_update"] = "⬆  Install update",
        ["btn.rename_folder"] = "✏  Rename folder",
        ["btn.choose_sidebar_crop"] = "🖼  Choose sidebar crop",
        ["btn.save_thread"] = "💾  Save thread & check",
        ["btn.open_thread"] = "🔗  Open thread",
        ["btn.save_global"] = "💾  Save global settings",
        ["btn.login"] = "🔐  Log in",
        ["btn.logout"] = "🚪  Clear cookies",
        ["btn.open_f95"] = "↗  Open f95zone.to",
        ["btn.build"] = "▶  Build",
        ["btn.uninstall"] = "🗑  Uninstall",
        ["btn.decompile_rpyc"] = "🔓  Decompile .rpyc",
        ["btn.crop_save"] = "💾  Save crop",
        ["btn.go"] = "▶  Go",

        // Placeholders
        ["placeholder.thread_url"] = "https://f95zone.to/threads/…",
        ["placeholder.username"] = "Username / email",
        ["placeholder.password"] = "Password",
        ["placeholder.saves_filter"] = "Filter variable (e.g. money, love, points) …",
        ["placeholder.new_name"] = "(unchanged)",

        // Tooltips
        ["tooltip.rename_folder"] =
            "Renames the container folder on disk. .renpyassist/ metadata " +
            "travels with it. Sidebar tile and detail view get re-keyed.",
        ["tooltip.choose_crop"] =
            "Opens a dialog with the original cover — drag the 2:3 frame " +
            "and save. The crop becomes the sidebar tile.",
        ["tooltip.open_thread"] =
            "Opens the linked f95zone thread in the default browser. "
            + "Disabled without a linked thread — enter the link in settings (⚙).",
        ["tooltip.inline_play"] = "MJPEG frame stream via ffmpeg (no audio, 12 fps)",
        ["tooltip.inline_stop"] = "Stop inline playback",
        ["tooltip.open_external"] = "Open in the system default player (VLC/mpv/…)",
        ["tooltip.decompile_rpyc"] =
            "Decompiles all .rpyc files in the active game/ folder to .rpy " +
            "(ported from RenPack). Existing up-to-date .rpy files are skipped.",

        // Section labels
        ["section.thread_url"] = "f95zone thread URL",
        ["section.thread_help"] =
            "Link to the f95zone thread. When set: version checks, cover, " +
            "description and genre are loaded automatically.",
        ["section.actions"] = "Actions",
        ["section.plugin_global_header"] = "Plugin settings (global)",
        ["section.downloads_dir"] = "Downloads watch folder",
        ["section.interval"] = "Update check interval (minutes)",
        ["section.f95_login"] = "f95zone login",
        ["section.f95_login_help"] =
            "Login is optional but recommended for cover downloads. Password is " +
            "NEVER stored — only session cookies, encrypted via host secrets.",
        ["section.description"] = "Description",
        ["section.genre"] = "Genre",
        ["section.mod_type"] = "Pick mod type",
        ["section.krostemod_title"] = "KrosteMod pipeline",
        ["section.krostemod_subtitle"] =
            "Ported from RenPack (Kroste original). Pick a type, click 'Build' — " +
            "the plugin decompiles all .rpyc, analyses the scripts, generates the " +
            "mod and deploys it into the game/ folder. Original .rpyc are backed up " +
            "as .krostemod-bak; uninstall restores them.",

        // Status messages
        ["status.game_dir_missing"] = "game/ folder not found: {0}",
        ["status.saves_dir_missing"] = "saves/ folder not found: {0}",
        ["status.rpa_scan_result"] = "{0} .rpa archive(s) found",
        ["status.loading_index"] = "Loading index: {0} …",
        ["status.index_summary"] = "{0} · {1} file(s) · {2}",
        ["status.index_error"] = "Index error: {0}",
        ["status.preview_too_large"] = " · too large for preview",
        ["status.preview_binary"] = " · binary (no preview)",
        ["status.preview_error"] = " · error: {0}",
        ["status.preview_image_decode_fail"] = " · image decode failed",
        ["status.saves_count"] = "{0} save(s) in folder {1}",
        ["status.loading_save"] = "Loading save: {0} …",
        ["status.vars_editable"] = "{0} variable(s) editable",
        ["status.log_error"] = "Log error: {0}",
        ["status.load_error"] = "Load error: {0}",
        ["status.mods_default"] = "Pick a mod type and click 'Build'.",
        ["status.building"] = "Building {0} …",
        ["status.mod_deployed"] = "✔ Mod deployed: {0} file(s) in {1}",
        ["status.error_prefix"] = "Error: {0}",
        ["status.searching_rpyc"] = "Searching .rpyc files …",
        ["status.decompile_progress"] = "Decompiling {0}/{1} …",
        ["status.decompile_ok"] = "✔ {0} decompiled, {1} skipped (already up to date).",
        ["status.decompile_partial"] = "⚠ {0} OK, {1} errors, {2} skipped — check log.",
        ["status.krostemod_none"] = "No KrosteMod active.",
        ["status.krostemod_installed"] = "KrosteMod installed (manifest: {0})",
        ["status.uninstalling"] = "Uninstalling …",
        ["status.uninstall_ok"] = "✔ {0} file(s) removed, {1} backup(s) restored.",
        ["status.no_krostemod"] = "No KrosteMod installed — nothing to uninstall.",
        ["status.no_thread_check"] = "No thread URL — nothing to check.",
        ["status.checking_thread"] = "Checking thread …",
        ["status.check_done"] = "Check done.",
        ["status.check_error"] = "Check error: {0}",
        ["status.unpacking"] = "Unpacking …",
        ["status.update_done"] = "Update done.",
        ["status.status_error_prefix"] = "Error: {0}",
        ["status.link_removed"] = "Link removed.",
        ["status.thread_saved"] = "Thread saved — checking now …",
        ["status.renaming"] = "Renaming folder …",
        ["status.renamed"] = "Folder renamed.",
        ["status.rename_fail"] = "Rename failed.",
        ["status.global_saved"] = "Saved at {0}.",
        ["status.login_logging_in"] = "Logging in …",
        ["status.login_missing_creds"] = "⚠ Please enter username + password.",
        ["status.login_ok"] = "✔ Logged in as {0}",
        ["status.login_fail"] = "✘ Login failed (wrong credentials?)",
        ["status.login_error"] = "✘ Error: {0}",
        ["status.login_prefix_fail"] = "✘ {0}",
        ["status.logout_done"] = "✘ Not logged in (cookies cleared — plugin restart required)",
        ["status.login_logged_in_as"] = "✔ Logged in as {0}",
        ["status.login_logged_in_unknown"] = "(unknown)",
        ["status.login_not_logged_in"] = "✘ Not logged in",
        ["status.container_prefix"] = "Container: {0}",
        ["status.last_checked_prefix"] = "last checked: {0}",
        ["status.no_thread_hint"] =
            "No f95zone thread linked. In settings (⚙) upper right " +
            "you can enter the thread link — then cover, " +
            "description, genre and update checks appear automatically.",
        ["status.desc_will_load"] =
            "(Description will load on the next thread check — click 🔄 Check in settings.)",
        ["status.desc_translating"] = "(Original — AI translating …)",
        ["status.desc_translated_cached"] = "(AI translated, cached — original see settings)",
        ["status.desc_translated"] = "(AI translated)",
        ["status.desc_translation_unavailable"] = "(AI not available or translation empty — showing original)",
        ["status.subpath_prefix"] = "Sub-path: {0}",
        ["status.version_line"] = "local: {0}  ·  remote: {1}",
        ["status.update_badge"] = "↑ Update",
        ["status.update_badge_with_version"] = "↑ {0}",

        // Notifications
        ["notify.new_zip"] = "New Ren'Py ZIP: {0}",
        ["notify.extern_opened"] = "Opened external: {0}",
        ["notify.extracted"] = "Extracted: {0}",
        ["notify.extracted_count"] = "{0} file(s) extracted from {1}",
        ["notify.saves_no_changes"] = "No changes to save",
        ["notify.saves_changes_saved"] = "{0} change(s) saved",
        ["notify.thread_linked"] = "Thread linked: {0}",
        ["notify.update_installed"] = "Update installed: {0} → {1}",
        ["notify.settings_saved"] = "Settings saved",
        ["notify.f95_login_ok"] = "f95zone login successful",
        ["notify.f95_cookies_cleared"] = "f95zone cookies cleared",
        ["notify.folder_renamed"] = "Folder renamed: \"{0}\" → \"{1}\"",
        ["notify.folder_renamed_pending"] = "Folder renamed (host tile will follow on restart)",
        ["notify.sidebar_crop_saved"] = "Sidebar crop saved and applied.",
        ["notify.mod_installed"] = "{0} for {1} installed ({2} file(s))",
        ["notify.krostemod_uninstalled"] = "KrosteMod uninstalled",
        ["notify.decompile_summary"] = "Decompile: {0}/{1} .rpyc → .rpy",
        ["notify.decompile_error"] = "Decompile error: {0}",

        // Dialogs
        ["dialog.confirm"] = "Confirm",
        ["dialog.error"] = "Error",
        ["dialog.remove_link_title"] = "Remove link",
        ["dialog.remove_link_msg"] = "Remove thread URL for \"{0}\"?",
        ["dialog.invalid_url_title"] = "URL invalid",
        ["dialog.invalid_url_msg"] = "Please enter a full http(s):// URL.",
        ["dialog.install_update_title"] = "Install update",
        ["dialog.install_update_msg"] =
            "ZIP will be extracted into \"{0}\". Save games are copied from the old " +
            "sub-folder into the new one. The old sub-folder is then removed. " +
            "The ZIP file is archived in \"archive/\".\n\nContinue?",
        ["dialog.install_fail_title"] = "Install failed",
        ["dialog.install_fail_unknown"] = "Unknown error.",
        ["dialog.no_cover_title"] = "No cover",
        ["dialog.no_cover_msg"] =
            "There is no cover image to crop yet. First enter a f95zone " +
            "thread and click 🔄 Check.",
        ["dialog.no_launcher_title"] = "No launcher",
        ["dialog.no_launcher_msg"] = "No .sh/.exe found in active sub-folder:\n{0}",
        ["dialog.rename_title"] = "Rename folder",
        ["dialog.rename_missing_msg"] = "Container folder does not exist:\n{0}",
        ["dialog.rename_prompt"] =
            "New folder name for \"{0}\":\n(The container folder on disk will be renamed. " +
            "Container-local metadata in .renpyassist/ travels with it.)",
        ["dialog.rename_invalid_chars"] =
            "The name contains invalid characters (/, \\, :, *, ?, \", <, >, |).",
        ["dialog.rename_target_exists"] = "Target path already exists:\n{0}",
        ["dialog.rename_fail_msg"] = "Rename failed:\n{0}",
        ["dialog.pick_downloads"] = "Pick downloads folder",
        ["dialog.pick_folder_extract"] = "Target folder for extraction",
        ["dialog.pick_folder_extract_all"] = "Target folder for full archive",
        ["dialog.pick_update_zip"] = "Update ZIP for \"{0}\"",
        ["dialog.zip_filter"] = "Ren'Py ZIPs",
        ["dialog.extract_all_title"] = "Extract all?",
        ["dialog.extract_all_msg"] = "{0} file(s) will be extracted to\n{1}\nContinue?",
        ["dialog.extract_error_title"] = "Extract error",
        ["dialog.save_overwrite_title"] = "Overwrite save?",
        ["dialog.save_overwrite_msg"] =
            "{0} variable(s) will be patched in save \"{1}\".\n" +
            "Ren'Py saves are byte-preserving edited — roundtrip-safe. Still: " +
            "backup recommended.\n\nContinue?",
        ["dialog.save_error_title"] = "Save error",
        ["dialog.value_invalid_title"] = "Value invalid",
        ["dialog.value_invalid_msg"] = "\"{0}\": \"{1}\" is not a valid Python literal.",
        ["dialog.build_confirm_title"] = "Build {0} mod?",
        ["dialog.build_confirm_msg"] =
            "The mod will be built for \"{0}\" and deployed into " +
            "\"{1}\". All original .rpyc are backed up as .krostemod-bak. " +
            "Uninstall via \"🗑 Uninstall\" — restores originals.\n\n" +
            "Continue?",
        ["dialog.mods_game_dir_title"] = "game/ folder not found",
        ["dialog.mods_game_dir_msg"] = "Expected: {0}",
        ["dialog.krostemod_uninstall_title"] = "Uninstall KrosteMod?",
        ["dialog.krostemod_uninstall_msg"] =
            "All modified .rpy will be deleted, .rpyc backups (.krostemod-bak) will be " +
            "restored.\n\nContinue?",

        // Cover-Crop-Dialog
        ["crop.title"] = "Pick sidebar tile crop",
        ["crop.info"] =
            "Drag the gold frame with the mouse, size via slider. " +
            "Target ratio 2:3 (Steam library portrait, 600×900).",
        ["crop.zoom_label"] = "Zoom",
        ["crop.status_load_fail"] = "Image load failed: {0}",
        ["crop.status_save_fail"] = "Save error: {0}",
        ["crop.status_summary"] = "Crop: {0}×{1} px  (position {2},{3} · original {4}×{5})",

        // Rename-Dialog
        ["rename.title"] = "Rename character",
        ["rename.header"] = "{0} characters detected in game",
        ["rename.help"] =
            "Enter new names in the right column. Empty text = no change. " +
            "After Apply: plugin rewrites character object names and (if Ollama/" +
            "cloud configured) has the AI rewrite body texts consistently " +
            "(grammar, relationship words).",

        // Translate-Setup-Dialog
        ["translate.title"] = "Set up translation",
        ["translate.header"] = "🌐  Translate Ren'Py game",
        ["translate.stats"] =
            "{0} dialog lines in game · {1} unique texts " +
            "(after dedup) → translated via AI batch (30 texts/batch).",
        ["translate.time_estimate"] =
            "Time estimate: Ollama ~5-10 s/batch, cloud ~2-3 s/batch. " +
            "At 500 says ≈ 20 batches → 1-3 min.",
        ["translate.target_lang"] = "Target language",
        ["translate.status"] = "🌐 AI translation ({0}) running …",
        ["translate.progress_label"] = "AI translation {0}",
        ["translate.fail"] = "AI translation failed: {0}",

        // Mod-Types
        ["mod.walkthrough.name"] = "Walkthrough",
        ["mod.walkthrough.desc"] =
            "Shows best options in choice menus — variable-based via regex analysis.",
        ["mod.cheat.name"] = "Cheat",
        ["mod.cheat.desc"] =
            "F11 overlay in game: edit all store variables live (money, relationship values, flags).",
        ["mod.rename.name"] = "Rename",
        ["mod.rename.desc"] =
            "Character rename with editor dialog (old→new). If AI configured: " +
            "body texts are rewritten consistently (grammar, relationship words).",
        ["mod.translate.name"] = "Translate",
        ["mod.translate.desc"] =
            "AI batch translation of all dialogs into a target language. Needs host AI " +
            "(Ollama/cloud). Ollama: ~5-10 s/batch, cloud: ~2-3 s/batch. At 500 says ≈ 1-3 min.",

        // Archive-Row
        ["archive.summary"] = "{0}  ·  {1}  ·  {2} files",

        // Saves metadata labels
        ["saves.meta.slot"] = "Slot: {0}",
        ["saves.meta.time"] = "Time: {0}",
        ["saves.meta.game"] = "Game: {0}",
        ["saves.meta.renpy"] = "Ren'Py: {0}",

        // ffmpeg-Missing-Hint
        ["hint.ffmpeg_missing"] =
            "ffmpeg missing — for inline playback please install:\n" +
            "Fedora/Bazzite: sudo dnf install ffmpeg-free\n" +
            "Debian/Ubuntu: sudo apt install ffmpeg\n" +
            "Windows: winget install ffmpeg\n\n" +
            "The external player still works.",

        // Launcher/Update notify
        ["notify.game_started"] = "Ren'Py game started: {0}",
        ["notify.game_start_fail"] = "Start failed: {0}",
        ["notify.no_launcher"] = "No Ren'Py launcher found in \"{0}\".",
        ["notify.update_thread_opened"] = "Update {0} for \"{1}\" — thread opened in browser.",
        ["notify.update_available_summary"] = "Ren'Py update available: {0}",
    };
}
