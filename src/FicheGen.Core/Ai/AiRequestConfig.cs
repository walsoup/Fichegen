namespace FicheGen.Core.Ai;

public sealed record RoutingOverride(string Provider, string? Model = null);

public sealed record AiRequestConfig(
    string GlobalProvider,
    IReadOnlyDictionary<string, string> DefaultModels,
    IReadOnlyDictionary<string, RoutingOverride> RoutingOverrides,
    string ProxyBaseUrl,
    string VertexProject,
    string VertexRegion,
    IReadOnlyDictionary<string, double> Temperatures,
    Func<string, CancellationToken, ValueTask<string?>> SecretResolver)
{
}

