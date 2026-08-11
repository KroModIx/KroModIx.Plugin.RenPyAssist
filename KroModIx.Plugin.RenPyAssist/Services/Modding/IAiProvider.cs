using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.RenPyAssist.Services.Modding;

/// <summary>Abstraktion für die KI-Anfragen der Mod-Generatoren
/// (<see cref="KrosteAiTranslator"/>, <see cref="KrosteAiRewriter"/>). RenPack
/// nutzte hier ein eigenes Interface — im KroModIx-Plugin bekommen wir das
/// über den Host-<see cref="IAiService"/>. Der <see cref="HostAiProviderAdapter"/>
/// leitet 1:1 an <see cref="IHostServices.Ai"/> weiter.</summary>
public interface IAiProvider
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

/// <summary>Adapter: leitet <see cref="IAiProvider.CompleteAsync"/> an den
/// Host-KI-Provider (<see cref="IAiService"/>). Kein zusätzliches
/// Provider-Setup im Plugin — die Config liegt zentral im Host.</summary>
public sealed class HostAiProviderAdapter : IAiProvider
{
    private readonly IHostServices _host;
    public HostAiProviderAdapter(IHostServices host) => _host = host;

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        => _host.Ai.CompleteAsync(systemPrompt, userPrompt, ct);
}
