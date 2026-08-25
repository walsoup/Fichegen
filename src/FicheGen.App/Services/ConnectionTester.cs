using System.Diagnostics;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Ai;
using FicheGen.Core.Storage;
using FicheGen.Infrastructure.Ai;

namespace FicheGen.App.Services;

/// <summary>Résultat d'un test de connexion fournisseur.</summary>
public sealed record ConnectionTestResult(bool Success, int LatencyMs, string Message);

public interface IConnectionTester
{
    /// <summary>
    /// Exécute une véritable requête minimale auprès du fournisseur afin de
    /// valider connectivité + authentification de bout en bout.
    /// </summary>
    Task<ConnectionTestResult> TestAsync(string providerKey, CancellationToken cancellationToken = default);
}

/// <summary>Test de connexion réel : petite requête de génération (quelques tokens)
/// routée vers le fournisseur demandé via <see cref="LlmRouter"/>.</summary>
public sealed class ConnectionTester : IConnectionTester
{
    private const int TimeoutSeconds = 25;

    private readonly ILlmClient _llmClient;
    private readonly ISettingsStore _settingsStore;
    private readonly ICredentialStore? _credentialStore;

    public ConnectionTester(ILlmClient llmClient, ISettingsStore settingsStore, ICredentialStore? credentialStore = null)
    {
        _llmClient = llmClient;
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
    }

    public async Task<ConnectionTestResult> TestAsync(string providerKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
            return new ConnectionTestResult(false, 0, "Fournisseur non spécifié.");

        var settings = _settingsStore.GetSettings<AppSettings>();

        var config = new AiRequestConfig(
            GlobalProvider: settings.Ai.GlobalProvider,
            DefaultModels: new Dictionary<string, string>(settings.Ai.Models, StringComparer.OrdinalIgnoreCase),
            RoutingOverrides: new Dictionary<string, RoutingOverride>
            {
                ["connection_test"] = new RoutingOverride(providerKey)
            },
            ProxyBaseUrl: settings.Ai.ProxyBaseUrl,
            VertexProject: settings.Ai.Vertex.Project,
            VertexRegion: settings.Ai.Vertex.Region,
            Temperatures: new Dictionary<string, double>(),
            SecretResolver: (key, _) => ValueTask.FromResult(_credentialStore?.Get(key)));

        var request = new LlmRequest(
            Purpose: "connection_test",
            SystemPrompt: "Ceci est un test de connexion. Réponds uniquement : OK",
            UserPrompt: "Ping",
            Temperature: 0,
            ResponseJson: false);

        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        try
        {
            await _llmClient.GenerateAsync(request, config, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new ConnectionTestResult(true, (int)stopwatch.ElapsedMilliseconds, "Connecté");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ConnectionTestResult(false, 0, $"Délai dépassé — pas de réponse en {TimeoutSeconds} s.");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, 0, ErrorMessageTranslator.ToUserFriendlyMessage(ex));
        }
    }
}
