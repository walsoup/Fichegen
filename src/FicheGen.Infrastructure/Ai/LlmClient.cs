using System.Runtime.CompilerServices;
using FicheGen.Core.Ai;
using FicheGen.Core.Ai.Routing;
using FicheGen.Infrastructure.Ai.Adapters;
using FicheGen.Infrastructure.Ai.Resilience;
using FicheGen.Infrastructure.Vertex;
using Polly;

namespace FicheGen.Infrastructure.Ai;

public sealed class LlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly LlmRouter _router;
    private readonly VertexTokenProvider _vertexTokenProvider;
    private readonly Dictionary<ProviderAdapterKind, IProviderAdapter> _adapters;

    public LlmClient(HttpClient httpClient, LlmRouter? router = null, VertexTokenProvider? vertexTokenProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _router = router ?? new LlmRouter();
        _vertexTokenProvider = vertexTokenProvider ?? new VertexTokenProvider();

        var adapters = new IProviderAdapter[]
        {
            new GeminiAdapter(),
            new VertexAdapter(),
            new OpenAiCompatibleAdapter(),
            new VercelAdapter()
        };

        _adapters = adapters.ToDictionary(a => a.Kind);
    }

    public async Task<string> GenerateAsync(LlmRequest req, AiRequestConfig cfg, CancellationToken ct)
    {
        var route = _router.Resolve(req.Purpose, cfg, isStreaming: false);

        if (!_adapters.TryGetValue(route.AdapterKind, out var adapter))
        {
            throw new LlmException(LlmExceptionKind.Configuration, $"Adaptateur non trouvé pour {route.AdapterKind}");
        }

        var secretKey = await ResolveSecretAsync(route, cfg, ct).ConfigureAwait(false);
        var temperature = GetTemperature(req, cfg);

        var pipeline = ResiliencePipelines.CreateNonStreamingPipeline();

        return await pipeline.ExecuteAsync(async cancellationToken =>
        {
            using var requestMessage = adapter.BuildRequest(req, route, secretKey, temperature);
            using var responseMessage = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
            return await adapter.ParseResponseAsync(responseMessage, cancellationToken).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        LlmRequest req,
        AiRequestConfig cfg,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var route = _router.Resolve(req.Purpose, cfg, isStreaming: true);

        if (!_adapters.TryGetValue(route.AdapterKind, out var adapter))
        {
            throw new LlmException(LlmExceptionKind.Configuration, $"Adaptateur non trouvé pour {route.AdapterKind}");
        }

        var secretKey = await ResolveSecretAsync(route, cfg, ct).ConfigureAwait(false);
        var temperature = GetTemperature(req, cfg);

        var pipeline = ResiliencePipelines.CreateStreamingPipeline();

        using var requestMessage = adapter.BuildRequest(req, route, secretKey, temperature);
        using var responseMessage = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        var rawStream = adapter.ParseStreamAsync(responseMessage, ct);

        await foreach (var chunk in StreamingChannelPipeline.BatchThrottledAsync(rawStream, TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    private async ValueTask<string?> ResolveSecretAsync(ProviderRoute route, AiRequestConfig cfg, CancellationToken ct)
    {
        if (cfg.SecretResolver == null)
            return null;

        var rawSecret = await cfg.SecretResolver(route.SecretKeyName, ct).ConfigureAwait(false);

        if (route.AdapterKind == ProviderAdapterKind.Vertex && !string.IsNullOrWhiteSpace(rawSecret))
        {
            return await _vertexTokenProvider.GetTokenAsync(rawSecret, ct).ConfigureAwait(false);
        }

        return rawSecret;
    }

    private static double GetTemperature(LlmRequest req, AiRequestConfig cfg)
    {
        if (req.Temperature > 0)
            return req.Temperature;

        if (cfg.Temperatures.TryGetValue(req.Purpose, out var temp))
            return temp;

        return 0.7;
    }
}
