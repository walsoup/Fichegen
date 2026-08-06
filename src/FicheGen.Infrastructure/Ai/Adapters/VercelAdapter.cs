using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using FicheGen.Core.Ai;
using FicheGen.Core.Ai.Routing;

namespace FicheGen.Infrastructure.Ai.Adapters;

public sealed class VercelAdapter : IProviderAdapter
{
    public ProviderAdapterKind Kind => ProviderAdapterKind.Vercel;

    public HttpRequestMessage BuildRequest(LlmRequest req, ProviderRoute route, string? secretKey, double temperature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route.Endpoint);

        if (!string.IsNullOrEmpty(secretKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        }

        var bodyObj = new
        {
            prompt = req.UserPrompt,
            system = req.SystemPrompt,
            temperature = temperature
        };

        var json = System.Text.Json.JsonSerializer.Serialize(bodyObj);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return request;
    }

    public async Task<string> ParseResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, content);
        return content;
    }

    public async IAsyncEnumerable<string> ParseStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await foreach (var chunk in PlainTextStreamParser.ReadTextChunksAsync(stream, ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(chunk))
            {
                yield return chunk;
            }
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string content)
    {
        if (!response.IsSuccessStatusCode)
        {
            TimeSpan? retryAfter = null;
            if (response.Headers.RetryAfter?.Delta.HasValue == true)
            {
                retryAfter = response.Headers.RetryAfter.Delta.Value;
            }

            var statusCode = (int)response.StatusCode;
            var kind = statusCode switch
            {
                401 or 403 => LlmExceptionKind.Auth,
                429 => LlmExceptionKind.RateLimited,
                _ => LlmExceptionKind.Provider
            };

            throw new LlmException(kind, $"Vercel AI SDK endpoint returned status {response.StatusCode}: {content}", statusCode, retryAfter);
        }
    }
}
