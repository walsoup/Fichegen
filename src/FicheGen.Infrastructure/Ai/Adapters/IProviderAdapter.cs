using FicheGen.Core.Ai;
using FicheGen.Core.Ai.Routing;

namespace FicheGen.Infrastructure.Ai.Adapters;

public interface IProviderAdapter
{
    ProviderAdapterKind Kind { get; }

    HttpRequestMessage BuildRequest(LlmRequest req, ProviderRoute route, string? secretKey, double temperature);

    Task<string> ParseResponseAsync(HttpResponseMessage response, CancellationToken ct);

    IAsyncEnumerable<string> ParseStreamAsync(HttpResponseMessage response, CancellationToken ct);
}
