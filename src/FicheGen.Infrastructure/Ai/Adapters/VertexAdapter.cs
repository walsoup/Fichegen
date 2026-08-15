using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FicheGen.Core.Ai;
using FicheGen.Core.Ai.Routing;

namespace FicheGen.Infrastructure.Ai.Adapters;

public sealed class VertexAdapter : IProviderAdapter
{
    public ProviderAdapterKind Kind => ProviderAdapterKind.Vertex;

    public HttpRequestMessage BuildRequest(LlmRequest req, ProviderRoute route, string? secretKey, double temperature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route.Endpoint);

        if (!string.IsNullOrEmpty(secretKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        }

        var bodyObj = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = req.UserPrompt } }
                }
            },
            systemInstruction = string.IsNullOrWhiteSpace(req.SystemPrompt)
                ? null
                : new { parts = new[] { new { text = req.SystemPrompt } } },
            generationConfig = new
            {
                temperature = temperature,
                responseMimeType = req.ResponseJson ? "application/json" : null
            }
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
        return ExtractTextFromCandidates(doc.RootElement);
    }

    public async IAsyncEnumerable<string> ParseStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            EnsureSuccess(response, errorContent);
        }

        var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        await foreach (var sseData in SseStreamParser.ReadDataEventsAsync(contentStream, ct).ConfigureAwait(false))
        {
            if (sseData == "[DONE]")
                break;

            string? chunkText = null;
            try
            {
                using var doc = JsonDocument.Parse(sseData);
                if (doc.RootElement.TryGetProperty("error", out var errorEl))
                {
                    var errorMsg = errorEl.TryGetProperty("message", out var m) ? m.GetString() : errorEl.ToString();
                    throw new LlmException(LlmExceptionKind.Provider, $"Erreur de stream Vertex AI: {errorMsg}");
                }
                chunkText = ExtractTextFromCandidates(doc.RootElement);
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

    private static string ExtractTextFromCandidates(JsonElement root)
    {
        var sb = new StringBuilder();
        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textElem))
                        {
                            sb.Append(textElem.GetString());
                        }
                    }
                }
            }
        }
        return sb.ToString();
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

            throw new LlmException(kind, $"Vertex AI returned status {response.StatusCode}: {content}", statusCode, retryAfter);
        }
    }
}
