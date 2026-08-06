using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FicheGen.Core.Ai;
using FicheGen.Core.Ai.Routing;

namespace FicheGen.Infrastructure.Ai.Adapters;

public sealed class OpenAiCompatibleAdapter : IProviderAdapter
{
    public ProviderAdapterKind Kind => ProviderAdapterKind.OpenAiCompatible;

    public HttpRequestMessage BuildRequest(LlmRequest req, ProviderRoute route, string? secretKey, double temperature)
    {
        var isStreaming = route.Endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase);
        var request = new HttpRequestMessage(HttpMethod.Post, route.Endpoint);

        if (!string.IsNullOrEmpty(secretKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        }

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(req.SystemPrompt))
        {
            messages.Add(new { role = "system", content = req.SystemPrompt });
        }
        messages.Add(new { role = "user", content = req.UserPrompt });

        var bodyObj = new
        {
            model = route.Model,
            messages = messages,
            temperature = temperature,
            stream = isStreaming,
            response_format = req.ResponseJson ? new { type = "json_object" } : null
        };

        var json = JsonSerializer.Serialize(bodyObj, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return request;
    }

    public async Task<string> ParseResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, content);

        using var doc = JsonDocument.Parse(content);
        return ExtractContent(doc.RootElement);
    }

    public async IAsyncEnumerable<string> ParseStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken ct)
    {
        var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        await foreach (var sseData in SseStreamParser.ReadDataEventsAsync(contentStream, ct).ConfigureAwait(false))
        {
            if (sseData == "[DONE]")
                break;

            string? chunkText = null;
            try
            {
                using var doc = JsonDocument.Parse(sseData);
                chunkText = ExtractDeltaContent(doc.RootElement);
            }
            catch (JsonException)
            {
                chunkText = sseData;
            }

            if (!string.IsNullOrEmpty(chunkText))
            {
                yield return chunkText;
            }
        }
    }

    private static string ExtractContent(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? string.Empty;
            }
        }
        return string.Empty;
    }

    private static string ExtractDeltaContent(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? string.Empty;
            }
            if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var msgContent))
            {
                return msgContent.GetString() ?? string.Empty;
            }
        }
        return string.Empty;
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

            throw new LlmException(kind, $"OpenAI API returned status {response.StatusCode}: {content}", statusCode, retryAfter);
        }
    }
}
