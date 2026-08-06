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
    string SecretKeyName)
{
}

