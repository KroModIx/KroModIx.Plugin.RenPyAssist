using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>„Windows-only-Build nativ lauffähig machen" als Sidebar-Aktion.
///
/// <para>Für Spiele, die schon installiert sind — bei neu angelegten läuft
/// das Nachrüsten automatisch mit. Die Aktion sucht sich die betroffenen
/// Builds selbst zusammen: wer sie braucht, weiß meist nicht, welches seiner
/// Spiele betroffen ist.</para></summary>
public sealed class FixRuntimeAction : IGlobalAction
{
    private readonly GamesRegistry _registry;
    private readonly LinuxRuntimeInstaller _installer;
    private readonly IHostServices _host;

    public FixRuntimeAction(GamesRegistry registry, LinuxRuntimeInstaller installer,
        IHostServices host)
    {
        _registry = registry;
        _installer = installer;
        _host = host;
    }

    public string Id => "fix-linux-runtime";
    public string Label => Strings.T("action.fix_runtime");
    public string Icon => "\U0001F427"; // 🐧
    public string? Description => Strings.T("action.fix_runtime_hint");
    public int Order => 20;

    /// <summary>Nur unter Linux sinnvoll — unter Windows fehlt nichts.</summary>
    public bool IsEnabled => OperatingSystem.IsLinux() && _registry.Games.Count > 0;

    public async Task InvokeAsync(CancellationToken ct = default)
    {
        var betroffen = FindAffected(_registry).ToList();
        if (betroffen.Count == 0)
        {
            await _host.Dialogs.ShowMessageAsync(
                Strings.T("runtime.found_title"), Strings.T("runtime.none_found"));
            return;
        }

        var namen = string.Join(", ", betroffen.Select(x => x.Game.DisplayName));
        var version = betroffen[0].Inspection.RenPyVersion ?? "?";
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("runtime.found_title"),
            string.Format(Strings.T("runtime.confirm"), namen, version),
            okLabel: Strings.T("action.fix_runtime"),
            cancelLabel: Strings.T("btn.cancel"));
        if (!ok) return;

        foreach (var (game, buildDir, inspection) in betroffen)
        {
            ct.ThrowIfCancellationRequested();
            using var progress = _host.BeginProgress(game.DisplayName);
            var res = await _installer.FixAsync(buildDir, inspection,
                melde => progress.SetIndeterminate(melde), ct).ConfigureAwait(true);

            if (res.Success)
                _host.Notifications.Notify(
                    string.Format(Strings.T("runtime.done"), game.DisplayName,
                        GameUninstallService.FormatSize(res.BytesAdded)),
                    NotificationLevel.Success);
            else
                await _host.Dialogs.ShowMessageAsync(Strings.T("runtime.fail_title"),
                    $"{game.DisplayName}: {res.Error}");
        }
    }

    /// <summary>Alle registrierten Spiele, deren aktiver Build keine
    /// Linux-Runtime hat, aber eine Windows-Runtime — und bei denen genug
    /// bekannt ist, um das zu ändern.</summary>
    public static IEnumerable<(RenPyGame Game, string BuildDir, BuildInspection Inspection)>
        FindAffected(GamesRegistry registry)
    {
        foreach (var game in registry.Games)
        {
            var buildDir = string.IsNullOrEmpty(game.ActiveSubPath)
                ? game.ContainerPath
                : Path.Combine(game.ContainerPath, game.ActiveSubPath!);
            if (!Directory.Exists(buildDir)) continue;
            var inspection = RenPyBuildInspector.Inspect(buildDir);
            if (inspection.CanBeFixed) yield return (game, buildDir, inspection);
        }
    }
}
