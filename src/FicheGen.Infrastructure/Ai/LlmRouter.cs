using FicheGen.Core.Ai;
using FicheGen.Core.Ai.Routing;

namespace FicheGen.Infrastructure.Ai;

public class LlmRouter
{
    public virtual ProviderRoute Resolve(string purpose, AiRequestConfig cfg, bool isStreaming = false)
    {
        string providerStr;
        string? modelOverride = null;

        var canonicalPurpose = NormalizePurpose(purpose);

        if (cfg.RoutingOverrides.TryGetValue(purpose, out var routingOverride) ||
            cfg.RoutingOverrides.TryGetValue(canonicalPurpose, out routingOverride))
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
            model = ResolveDefaultModel(providerStr, canonicalPurpose);
        }

        var (endpoint, authStrategy, secretKeyName) = BuildEndpointAndAuth(providerStr, providerKind, model, cfg, isStreaming);

        return new ProviderRoute(providerKind, model, endpoint, authStrategy, secretKeyName, isStreaming);
    }

    private static string NormalizePurpose(string purpose) => purpose.ToLowerInvariant() switch
    {
        "eval" => "evaluation",
        "chat" => "assistant",
        "generation" => "fiche",
        _ => purpose.ToLowerInvariant()
    };

    private static string ResolveDefaultModel(string providerStr, string purpose)
    {
        return providerStr.ToLowerInvariant() switch
        {
            "aistudio" or "gemini" => "gemini-2.0-flash",
            "openai" => "gpt-4o-mini",
            "anthropic" => "claude-3-5-sonnet-20241022",
            "deepseek" => "deepseek-chat",
            "groq" => "llama-3.3-70b-versatile",
            "mistral" => "mistral-small-latest",
            "openrouter" => "meta-llama/llama-3.3-70b-instruct",
            "together" => "meta-llama/Llama-3.3-70B-Instruct-Turbo",
            "fireworks" => "accounts/fireworks/models/llama-v3p3-70b-instruct",
            "cerebras" => "llama3.3-70b",
            "xai" => "grok-2-latest",
            "doubleword" => "doubleword-default",
            "vertex" or "vertexai" => "gemini-1.5-flash-002",
            "cloud" or "profstudio" => purpose,
            _ => purpose
        };
    }

    private static ProviderAdapterKind NormalizeProvider(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "cloud" or "profstudio" => ProviderAdapterKind.OpenAiCompatible,
            "aistudio" or "gemini" => ProviderAdapterKind.Gemini,
            "vertex" or "vertexai" => ProviderAdapterKind.Vertex,
            "vercel" => ProviderAdapterKind.Vercel,
            _ => ProviderAdapterKind.OpenAiCompatible
        };
    }

    private static (string Endpoint, AuthStrategyKind AuthStrategy, string SecretKeyName) BuildEndpointAndAuth(
        string providerStr, ProviderAdapterKind kind, string model, AiRequestConfig cfg, bool isStreaming)
    {
        var isSupabase = providerStr.Equals("cloud", StringComparison.OrdinalIgnoreCase) || providerStr.Equals("profstudio", StringComparison.OrdinalIgnoreCase);

        if (isSupabase)
        {
            return (
                "https://bbodlidtaosxeyeovixe.supabase.co/functions/v1/chat",
                AuthStrategyKind.BearerToken,
                "supabase_access_token"
            );
        }

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
                    ? $"https://{cfg.VertexRegion}-aiplatform.googleapis.com/v1/projects/{cfg.VertexProject}/locations/{cfg.VertexRegion}/publishers/google/models/{model}:streamGenerateContent?alt=sse"
                    : $"https://{cfg.VertexRegion}-aiplatform.googleapis.com/v1/projects/{cfg.VertexProject}/locations/{cfg.VertexRegion}/publishers/google/models/{model}:generateContent",
                AuthStrategyKind.BearerToken,
                "vertex_service_account"
            ),

            ProviderAdapterKind.OpenAiCompatible => (
                BuildProxyUrl(cfg.ProxyBaseUrl, "/chat/completions", providerStr),
                AuthStrategyKind.BearerToken,
                ResolveOpenAiCompatibleSecretKey(providerStr)
            ),

            ProviderAdapterKind.Vercel => (
                BuildProxyUrl(cfg.ProxyBaseUrl, "/api/chat", providerStr),
                AuthStrategyKind.BearerToken,
                "vercel_api_key"
            ),

            _ => throw new LlmException(LlmExceptionKind.Configuration, $"Fournisseur non supporté: {kind}")
        };
    }

    private static string ResolveOpenAiCompatibleSecretKey(string providerStr)
    {
        return providerStr.ToLowerInvariant() switch
        {
            "openai" => "openai_api_key",
            "deepseek" => "deepseek_api_key",
            "groq" => "groq_api_key",
            "mistral" => "mistral_api_key",
            "openrouter" => "openrouter_api_key",
            "together" => "together_api_key",
            "fireworks" => "fireworks_api_key",
            "cerebras" => "cerebras_api_key",
            "xai" => "xai_api_key",
            "doubleword" => "doubleword_api_key",
            "anthropic" => "anthropic_api_key",
            _ => "proxy_api_key"
        };
    }

    private static string BuildProxyUrl(string baseUrl, string path, string providerStr = "")
    {
        if (providerStr.Equals("cloud", StringComparison.OrdinalIgnoreCase) || providerStr.Equals("profstudio", StringComparison.OrdinalIgnoreCase))
        {
            return "https://bbodlidtaosxeyeovixe.supabase.co/functions/v1/chat";
        }

        var provider = providerStr.ToLowerInvariant();
        string targetBaseUrl = provider switch
        {
            "openai" => "https://api.openai.com/v1",
            "deepseek" => "https://api.deepseek.com/v1",
            "groq" => "https://api.groq.com/openai/v1",
            "mistral" => "https://api.mistral.ai/v1",
            "openrouter" => "https://openrouter.ai/api/v1",
            "together" => "https://api.together.xyz/v1",
            "fireworks" => "https://api.fireworks.ai/inference/v1",
            "cerebras" => "https://api.cerebras.ai/v1",
            "xai" => "https://api.x.ai/v1",
            "doubleword" => "https://api.doubleword.ai/v1",
            "anthropic" => "https://api.anthropic.com/v1",
            _ => string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434/v1" : baseUrl
        };

        var trimmed = targetBaseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/chat", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(path, StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return $"{trimmed}{path}";
    }
}
