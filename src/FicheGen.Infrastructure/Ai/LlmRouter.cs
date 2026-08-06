using FicheGen.Core.Ai;
using FicheGen.Core.Ai.Routing;

namespace FicheGen.Infrastructure.Ai;

public class LlmRouter
{
    public virtual ProviderRoute Resolve(string purpose, AiRequestConfig cfg, bool isStreaming = false)
    {
        string providerStr;
        string? modelOverride = null;

        if (cfg.RoutingOverrides.TryGetValue(purpose, out var routingOverride))
        {
            providerStr = routingOverride.Provider;
            modelOverride = routingOverride.Model;
        }
        else
        {
            providerStr = cfg.GlobalProvider;
        }

        var providerKind = NormalizeProvider(providerStr);

        string model;
        if (!string.IsNullOrWhiteSpace(modelOverride))
        {
            model = modelOverride;
        }
        else if (cfg.DefaultModels.TryGetValue(providerStr, out var defaultModel) && !string.IsNullOrWhiteSpace(defaultModel))
        {
            model = defaultModel;
        }
        else
        {
            model = GetFallbackModel(providerKind);
        }

        var (endpoint, authStrategy, secretKeyName) = BuildEndpointAndAuth(providerKind, model, cfg, isStreaming);

        return new ProviderRoute(providerKind, model, endpoint, authStrategy, secretKeyName);
    }

    private static ProviderAdapterKind NormalizeProvider(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "aistudio" or "gemini" => ProviderAdapterKind.Gemini,
            "vertex" or "vertexai" => ProviderAdapterKind.Vertex,
            "proxy" or "ollama" or "openai" => ProviderAdapterKind.OpenAiCompatible,
            "vercel" => ProviderAdapterKind.Vercel,
            _ => ProviderAdapterKind.Gemini
        };
    }

    private static string GetFallbackModel(ProviderAdapterKind kind)
    {
        return kind switch
        {
            ProviderAdapterKind.Gemini => "gemini-3.6-flash",
            ProviderAdapterKind.Vertex => "gemini-3.6-flash",
            ProviderAdapterKind.OpenAiCompatible => "qwen2.5",
            ProviderAdapterKind.Vercel => "default",
            _ => "gemini-3.6-flash"
        };
    }

    private static (string Endpoint, AuthStrategyKind AuthStrategy, string SecretKeyName) BuildEndpointAndAuth(
        ProviderAdapterKind kind, string model, AiRequestConfig cfg, bool isStreaming)
    {
        return kind switch
        {
            ProviderAdapterKind.Gemini => (
                isStreaming
                    ? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse"
                    : $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                AuthStrategyKind.ApiKeyHeader,
                "gemini_api_key"
            ),

            ProviderAdapterKind.Vertex => (
                isStreaming
                    ? $"https://{cfg.VertexRegion}-aiplatform.googleapis.com/v1/projects/{cfg.VertexProject}/locations/{cfg.VertexRegion}/publishers/google/models/{model}:streamGenerateContent"
                    : $"https://{cfg.VertexRegion}-aiplatform.googleapis.com/v1/projects/{cfg.VertexProject}/locations/{cfg.VertexRegion}/publishers/google/models/{model}:generateContent",
                AuthStrategyKind.BearerToken,
                "vertex_service_account"
            ),

            ProviderAdapterKind.OpenAiCompatible => (
                BuildProxyUrl(cfg.ProxyBaseUrl, "/chat/completions"),
                AuthStrategyKind.BearerToken,
                "proxy_api_key"
            ),

            ProviderAdapterKind.Vercel => (
                BuildProxyUrl(cfg.ProxyBaseUrl, "/api/chat"),
                AuthStrategyKind.BearerToken,
                "vercel_api_key"
            ),

            _ => throw new LlmException(LlmExceptionKind.Configuration, $"Fournisseur non supporté: {kind}")
        };
    }

    private static string BuildProxyUrl(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://localhost:11434/v1";

        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith(path, StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return $"{trimmed}{path}";
    }
}
