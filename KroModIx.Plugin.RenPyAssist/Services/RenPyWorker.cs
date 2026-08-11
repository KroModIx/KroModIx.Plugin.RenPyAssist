using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public RenPyWorker(GamesRegistry registry, F95zoneClient client, RenPySettingsService settings)
    {
        _registry = registry;
        _client = client;
        _settings = settings;
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
                if (string.IsNullOrWhiteSpace(game.CoverUrl) && !string.IsNullOrWhiteSpace(info.CoverUrl))
                    game.CoverUrl = info.CoverUrl;
                _registry.Update(game);
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

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { if (_loop is not null) await _loop; }
        catch { }
        _cts.Dispose();
    }
}
