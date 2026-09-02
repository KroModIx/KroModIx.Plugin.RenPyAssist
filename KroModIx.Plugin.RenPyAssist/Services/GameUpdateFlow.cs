using System;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Der gemeinsame „Update installieren"-Ablauf (v0.20.0) — genutzt
/// vom Update-Badge in der Übersicht und vom Button im Einstellungen-Tab.
///
/// <para>Vorher lag der Ablauf nur im Einstellungen-ViewModel. Mit dem
/// klickbaren Badge braucht ihn eine zweite Stelle, und ein zweites
/// Copy-Paste dieser Kette (Datei waehlen → bestaetigen → installieren →
/// Saves-Warnung → Badge-Refresh) wollte ich nicht.</para></summary>
public sealed class GameUpdateFlow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly GameUpdateInstaller _installer;
    private readonly GamesRegistry _registry;
    private readonly RenPySettingsService _settings;
    private readonly IHostServices _host;

    public GameUpdateFlow(GameUpdateInstaller installer, GamesRegistry registry,
        RenPySettingsService settings, IHostServices host)
    {
        _installer = installer;
        _registry = registry;
        _settings = settings;
        _host = host;
    }

    /// <summary>Sucht zuerst im Downloads-Ordner nach einem passenden Archiv.
    ///
    /// <para>Treffer → ein Dialog, der Dateiname und Ziel nennt. „Ja"
    /// installiert sofort. „Nein" heisst „nicht diese Datei" und oeffnet
    /// deshalb den Datei-Dialog, statt einfach abzubrechen — genau dafuer
    /// klickt man den Badge ja an.</para>
    ///
    /// <para>Kein Treffer → direkt der Datei-Dialog.</para>
    ///
    /// <para><paramref name="status"/> bekommt Zwischenstaende fuer die UI
    /// (darf null sein). Rueckgabe: true = installiert.</para></summary>
    public async Task<bool> InstallUpdateAsync(RenPyGame game, Action<string>? status = null)
    {
        string? archive = null;

        var downloadsDir = _settings.Current.DownloadsWatchDir;
        var candidates = UpdateArchiveFinder.Find(
            downloadsDir, game.DisplayName, game.LocalVersion, game.LastRemoteVersion);
        Log.Info("Update-Suche in {Dir} fuer {Game}: {N} Kandidat(en)",
            downloadsDir, game.DisplayName, candidates.Count);

        if (candidates.Count > 0)
        {
            var best = candidates[0];
            var versionText = best.Version is null
                ? Strings.T("update.version_unknown")
                : string.Format(Strings.T("update.version_prefix"), best.Version);
            var take = await _host.Dialogs.ConfirmAsync(
                Strings.T("dialog.update_found_title"),
                string.Format(Strings.T("dialog.update_found_msg"),
                    best.FileName, versionText, game.ContainerPath),
                okLabel: Strings.T("dialog.update_found_ok"));
            if (take) archive = best.FullPath;
        }

        if (archive is null)
        {
            status?.Invoke(Strings.T("status.pick_update_file"));
            // v0.21.0: Der Dialog geht im Downloads-Ordner auf (Contracts
            // v1.28+) — dort liegt die Datei ja, nur eben unter einem Namen,
            // den die Suche nicht sicher zuordnen konnte.
            archive = await _host.Dialogs.PickFileInAsync(
                string.Format(Strings.T("dialog.pick_update_zip"), game.DisplayName),
                downloadsDir,
                (Strings.T("dialog.zip_filter"), new[] { "*.zip", "*.rar", "*.7z" }));
            if (archive is null) return false;

            var ok = await _host.Dialogs.ConfirmAsync(Strings.T("dialog.install_update_title"),
                string.Format(Strings.T("dialog.install_update_msg"), game.ContainerPath));
            if (!ok) return false;
        }

        status?.Invoke(Strings.T("status.unpacking"));
        var result = await _installer.InstallAsync(game, archive);
        if (!result.Success)
        {
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.install_fail_title"),
                result.Error ?? Strings.T("dialog.install_fail_unknown"));
            status?.Invoke(string.Format(Strings.T("status.status_error_prefix"), result.Error));
            return false;
        }

        _host.Notifications.Notify(
            string.Format(Strings.T("notify.update_installed"), game.DisplayName, result.NewSubPath),
            NotificationLevel.Success);
        status?.Invoke(Strings.T("status.update_done"));

        // Saves konnten nicht uebernommen werden → der alte Ordner steht noch.
        // Das MUSS als Dialog kommen, nicht nur als Toast: sonst startet der
        // User die neue Version und wundert sich, warum die Spielstaende fehlen.
        if (result.Warning is string warning)
        {
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.saves_warning_title"), warning);
            status?.Invoke(warning);
        }

        // Sidebar-Kachel-Badge muss sofort verschwinden — ohne Trigger wuerde
        // die 60s-Periodik greifen.
        try { await _host.RequestUpdateBadgeRefreshAsync(); }
        catch (Exception ex) { _host.Logger.Debug(ex, "Badge-Refresh nach Update fehlgeschlagen"); }
        return true;
    }
}
