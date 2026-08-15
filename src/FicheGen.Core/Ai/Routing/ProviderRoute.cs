namespace FicheGen.Core.Ai.Routing;

public enum ProviderAdapterKind
{
    Gemini,
    Vertex,
    OpenAiCompatible,
    Vercel
}

public enum AuthStrategyKind
{
    ApiKeyHeader,
    ApiKeyQuery,
    BearerToken,
    None
}

public sealed record ProviderRoute(
    ProviderAdapterKind AdapterKind,
    string Model,
    string Endpoint,
    AuthStrategyKind AuthStrategy,
    string SecretKeyName,
    bool IsStreaming = false)
{
    public override string ToString()
    {
        var safeEndpoint = System.Text.RegularExpressions.Regex.Replace(Endpoint, @"([?&]key=)[^&]+", "$1[REDACTED]");
        return $"ProviderRoute {{ AdapterKind = {AdapterKind}, Model = {Model}, Endpoint = {safeEndpoint}, AuthStrategy = {AuthStrategy}, SecretKeyName = {SecretKeyName}, IsStreaming = {IsStreaming} }}";
    }
}

