using System.Text.Json;
using FicheGen.Infrastructure.Ai;

namespace FicheGen.App.Services;

public interface IProxyModelScanner
{
    /// <summary>Interroge un point de terminaison OpenAI-compatible (ou Ollama)
    /// et retourne la liste des identifiants de modèles disponibles.</summary>
    Task<IReadOnlyList<string>> ScanAsync(string baseUrl, string? apiKey, CancellationToken cancellationToken = default);
}

/// <summary>Scan HTTP réel des modèles d'un proxy local / Ollama / endpoint compatible OpenAI.</summary>
public sealed class ProxyModelScanner : IProxyModelScanner
{
    private readonly System.Net.Http.HttpClient _httpClient;

    public ProxyModelScanner(System.Net.Http.IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(ProxyModelScanner));
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<IReadOnlyList<string>> ScanAsync(string baseUrl, string? apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Adresse du serveur requise.", nameof(baseUrl));

        var trimmed = baseUrl.TrimEnd('/');
        var requestUri = trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/models";

        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, requestUri);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        string content;
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (trimmed.Contains("11434"))
            {
                // Ollama natif : l'API tags remplace /v1/models.
                content = await _httpClient.GetStringAsync("http://localhost:11434/api/tags", cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException($"Impossible d'accéder à {requestUri} (HTTP {(int)response.StatusCode}).");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException($"Erreur de connexion : {ex.Message}", ex);
        }

        return ParseModels(content);
    }

    private static IReadOnlyList<string> ParseModels(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var models = new List<string>();

        // Format OpenAI : { "data": [ { "id": ... } ] }
        if (doc.RootElement.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataElem.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.String
                    && idElem.GetString() is { Length: > 0 } id)
                {
                    models.Add(id);
                }
            }
        }
        // Format Ollama : { "models": [ { "name": ... } ] }
        else if (doc.RootElement.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in modelsElem.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var nameElem) && nameElem.ValueKind == JsonValueKind.String
                    && nameElem.GetString() is { Length: > 0 } name)
                {
                    models.Add(name);
                }
            }
        }

        return models;
    }
}
