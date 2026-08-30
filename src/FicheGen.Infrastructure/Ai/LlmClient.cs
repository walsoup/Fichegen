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

    // Built once per client so circuit-breaker state persists across requests.
    private readonly ResiliencePipeline _nonStreamingPipeline;
    private readonly ResiliencePipeline _streamingPipeline;

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

        _nonStreamingPipeline = ResiliencePipelines.CreateNonStreamingPipeline();
        _streamingPipeline = ResiliencePipelines.CreateStreamingPipeline();
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

        return await _nonStreamingPipeline.ExecuteAsync(async cancellationToken =>
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

        // The pipeline covers connection establishment (transient 429/5xx/network
        // failures are retried there). Mid-stream failures cannot be retried
        // because chunks have already been yielded to the consumer.
        HttpResponseMessage? response = null;
        try
        {
            response = await _streamingPipeline.ExecuteAsync(async cancellationToken =>
            {
                using var requestMessage = adapter.BuildRequest(req, route, secretKey, temperature);
                var attempt = await _httpClient
                    .SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    // HttpClient does not throw on error statuses: without this
                    // check the retry/circuit-breaker policies would never see
                    // a 429/5xx raised at connection time.
                    EnsureConnectionSuccess(attempt);
                }
                catch
                {
                    attempt.Dispose();
                    throw;
                }

                return attempt;
            }, ct).ConfigureAwait(false);

            var rawStream = adapter.ParseStreamAsync(response, ct);

            await foreach (var chunk in StreamingChannelPipeline.BatchThrottledAsync(rawStream, TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false))
            {
                yield return chunk;
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static void EnsureConnectionSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        var statusCode = (int)response.StatusCode;
        var kind = statusCode switch
        {
            401 or 403 => LlmExceptionKind.Auth,
            429 => LlmExceptionKind.RateLimited,
            _ => LlmExceptionKind.Provider
        };

        throw new LlmException(kind, $"Provider returned status {response.StatusCode} before streaming started.", statusCode, retryAfter);
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
        if (cfg.Temperatures.TryGetValue(req.Purpose, out var temp))
            return temp;

        if (cfg.Temperatures.TryGetValue("generation", out var genTemp))
            return genTemp;

        if (req.Temperature > 0)
            return req.Temperature;

        return 0.7;
    }
}
