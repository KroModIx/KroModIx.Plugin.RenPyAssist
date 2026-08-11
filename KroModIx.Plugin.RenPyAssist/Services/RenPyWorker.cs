using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Hintergrund-Worker: pollt in konfigurierbarem Intervall
/// (Default 60 min) alle Spiele mit gesetztem <see cref="RenPyGame.ThreadUrl"/>
/// gegen f95zone und aktualisiert <see cref="RenPyGame.LastRemoteVersion"/>.
///
/// <para><b>Rate-Limiting:</b> Zwischen zwei Thread-Fetches liegt 1 s Pause,
/// damit wir f95zone nicht flood'en (Cloudflare-Trigger).</para>
///
/// <para><b>Bootstrap-Delay:</b> Beim Start warten wir 30 s, damit die
/// Plugin-Initialisierung + UI-Load fertig sind bevor die erste Netz-
/// Anfrage feuert.</para></summary>
public sealed class RenPyWorker : IAsyncDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly GamesRegistry _registry;
    private readonly F95zoneClient _client;
    private readonly RenPySettingsService _settings;
    private readonly CoverCache _covers;
    private readonly IHostServices _host;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public RenPyWorker(GamesRegistry registry, F95zoneClient client, RenPySettingsService settings,
        CoverCache covers, IHostServices host)
    {
        _registry = registry;
        _client = client;
        _settings = settings;
        _covers = covers;
        _host = host;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    /// <summary>Triggert einen sofortigen Check ausserhalb des Poll-Intervalls
    /// (z. B. für „Jetzt prüfen"-Button).</summary>
    public Task CheckNowAsync(CancellationToken ct = default) => CheckAllAsync(ct);

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAllAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Log.Warn(ex, "Worker-Iteration fehlgeschlagen");
            }

            var minutes = Math.Max(15, _settings.Current.CheckIntervalMinutes);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(minutes), ct);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task CheckAllAsync(CancellationToken ct)
    {
        var withThread = _registry.Games
            .Where(g => !string.IsNullOrWhiteSpace(g.ThreadUrl))
            .ToList();
        if (withThread.Count == 0) return;

        Log.Debug("Worker-Check: {N} Spiele mit ThreadUrl", withThread.Count);

        foreach (var game in withThread)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var info = await _client.FetchThreadInfoAsync(game.ThreadUrl!, ct);
                if (info is null) continue;
                game.LastRemoteVersion = info.Version;
                game.LastCheckedUtc = DateTime.UtcNow;
                // v0.10.1: CoverUrl immer aus dem aktuellen Thread-Fetch
                // uebernehmen (analog zu Description/Genres). Vorher: sticky-
                // Bug — sobald einmal irgendein Wert gesetzt war (z. B. aus
                // altem Container-Local-Store oder Copy), wurde eine neue URL
                // beim Thread-Wechsel NIE mehr uebernommen und die Kachel blieb
                // auf dem alten Cover haengen. Es gibt keinen User-facing
                // CoverUrl-Setter im Plugin, also kann kein "manueller Override"
                // ueberschrieben werden.
                bool coverUrlChanged = false;
                if (!string.IsNullOrWhiteSpace(info.CoverUrl)
                    && !string.Equals(game.CoverUrl, info.CoverUrl, StringComparison.Ordinal))
                {
                    game.CoverUrl = info.CoverUrl;
                    coverUrlChanged = true;
                }
                // v0.5: Description + Genre aus Thread übernehmen wenn gefunden
                if (!string.IsNullOrWhiteSpace(info.Description))
                    game.Description = info.Description;
                if (info.Genres.Count > 0)
                    game.Genres = new List<string>(info.Genres);
                _registry.Update(game);

                // v0.10.1: Cover proaktiv holen + Container-Mirror + Sidebar-
                // Refresh — sonst muesste der User erst die Detail-View oeffnen
                // damit LoadCoverAsync das neue Bild spiegelt.
                if (coverUrlChanged)
                    await WarmCoverAsync(game, ct);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Thread-Check fehlgeschlagen: {Url}", game.ThreadUrl);
            }
            // Rate-Limit: 1 s zwischen Requests
            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>v0.10.1: nach CoverUrl-Wechsel das neue Bild sofort holen,
    /// in den Container spiegeln und die Sidebar-Kachel refreshen. Sonst
    /// muesste der User erst die Detail-View oeffnen bevor
    /// <see cref="Views.RenPyGameViewModel.LoadCoverAsync"/> das macht.
    /// Wichtig: Container-Mirror wird VOR dem Fetch geloescht, damit ein
    /// eventueller App-Restart nicht die alte Datei propagiert.</summary>
    private async Task WarmCoverAsync(RenPyGame game, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(game.ContainerPath) || !Directory.Exists(game.ContainerPath))
            return;
        try
        {
            var mirror = GameLocalStore.CoverPath(game.ContainerPath);
            if (File.Exists(mirror)) { try { File.Delete(mirror); } catch { } }

            var cached = await _covers.EnsureAsync(game.CoverUrl!, ct);
            if (string.IsNullOrEmpty(cached)) return;
            var mirrored = GameLocalStore.CopyCoverIntoContainer(game.ContainerPath, cached);
            var sidebarOverride = GameLocalStore.SidebarCoverPath(game.ContainerPath);
            // User-Custom-Ausschnitt schlaegt Auto-Cover — bleibt bestehen.
            var effective = File.Exists(sidebarOverride) ? sidebarOverride
                          : (mirrored ?? cached);
            _host.TrySetManualGameCover(game.ContainerPath, effective);
            Log.Info("Cover aktualisiert: {Name} → {Path}", game.DisplayName, effective);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Cover-Warm fehlgeschlagen fuer {Name}", game.DisplayName);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { if (_loop is not null) await _loop; }
        catch { }
        _cts.Dispose();
    }
}
