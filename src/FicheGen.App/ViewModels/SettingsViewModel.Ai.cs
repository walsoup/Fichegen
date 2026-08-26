using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.App.Services;

namespace FicheGen.App.ViewModels;

public partial class SettingsViewModel
{
    // ─────────────── Onglet 2 · Fournisseurs IA & Routage ───────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStandardMode))]
    public partial bool EnableExpertMode { get; set; } = false;

    public bool IsStandardMode => !EnableExpertMode;

    [ObservableProperty] public partial string GlobalProvider { get; set; } = "cloud";
    [ObservableProperty] public partial ProviderOption? SelectedGlobalProvider { get; set; }

    public IReadOnlyList<ProviderOption> ProviderOptions => ProviderCatalog.Providers;

    // Clés d'API (Coffre d'identification Windows — jamais sur disque)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeminiKeyConfigured))]
    [NotifyPropertyChangedFor(nameof(IsGeminiKeyMissing))]
    public partial string GeminiApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenAiKeyConfigured))]
    [NotifyPropertyChangedFor(nameof(IsOpenAiKeyMissing))]
    public partial string OpenAiApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnthropicKeyConfigured))]
    [NotifyPropertyChangedFor(nameof(IsAnthropicKeyMissing))]
    public partial string AnthropicApiKey { get; set; } = string.Empty;

    [ObservableProperty] public partial string ProxyApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVercelKeyConfigured))]
    [NotifyPropertyChangedFor(nameof(IsVercelKeyMissing))]
    public partial string VercelApiKey { get; set; } = string.Empty;

    public bool IsGeminiKeyConfigured => !string.IsNullOrWhiteSpace(GeminiApiKey);
    public bool IsGeminiKeyMissing => !IsGeminiKeyConfigured;
    public bool IsOpenAiKeyConfigured => !string.IsNullOrWhiteSpace(OpenAiApiKey);
    public bool IsOpenAiKeyMissing => !IsOpenAiKeyConfigured;
    public bool IsAnthropicKeyConfigured => !string.IsNullOrWhiteSpace(AnthropicApiKey);
    public bool IsAnthropicKeyMissing => !IsAnthropicKeyConfigured;
    public bool IsVercelKeyConfigured => !string.IsNullOrWhiteSpace(VercelApiKey);
    public bool IsVercelKeyMissing => !IsVercelKeyConfigured;

    [ObservableProperty] public partial string ProxyBaseUrl { get; set; } = "http://localhost:11434/v1";
    [ObservableProperty] public partial string CustomEndpointModel { get; set; } = string.Empty;
    [ObservableProperty] public partial string VertexProject { get; set; } = string.Empty;
    [ObservableProperty] public partial string VertexRegion { get; set; } = "europe-west1";

    // États des tests de connexion (un par fournisseur)
    public ProviderConnectionState GeminiState { get; } = new("aistudio");
    public ProviderConnectionState OpenAiState { get; } = new("openai");
    public ProviderConnectionState AnthropicState { get; } = new("anthropic");
    public ProviderConnectionState ProxyState { get; } = new("proxy");
    public ProviderConnectionState VertexState { get; } = new("vertex");
    public ProviderConnectionState VercelState { get; } = new("vercel");

    // Matrice de routage — 7 tâches
    public ObservableCollection<ModelRoutingItem> RoutingMatrix { get; } = new();

    // Modèle d'intention
    [ObservableProperty] public partial string IntentModel { get; set; } = "gemini-3.6-flash";

    // Températures
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GenerationTemperatureLabel))]
    public partial double GenerationTemperature { get; set; } = 0.7;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntentTemperatureLabel))]
    public partial double IntentTemperature { get; set; } = 0.2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AssistantTemperatureLabel))]
    public partial double AssistantTemperature { get; set; } = 0.8;

    public string GenerationTemperatureLabel => DescribeTemperature(GenerationTemperature);
    public string IntentTemperatureLabel => DescribeTemperature(IntentTemperature);
    public string AssistantTemperatureLabel => DescribeTemperature(AssistantTemperature);

    partial void OnSelectedGlobalProviderChanged(ProviderOption? value)
    {
        if (value is not null) GlobalProvider = value.Key;
    }

    partial void OnGlobalProviderChanged(string value)
    {
        if (SelectedGlobalProvider?.Key != value)
            SelectedGlobalProvider = ProviderCatalog.Find(value);
    }

    partial void OnCustomEndpointModelChanged(string value)
    {
        foreach (var item in RoutingMatrix)
            item.ApplyCustomProxyModel(value);
    }

    private void ApplyRoutingValue(string taskKey, string? provider, string model)
    {
        var item = RoutingMatrix.FirstOrDefault(r => r.TaskKey == taskKey);
        if (item is null) return;
        if (!string.IsNullOrWhiteSpace(provider))
            item.SelectedProvider = ProviderCatalog.Find(provider);
        if (!string.IsNullOrWhiteSpace(model))
            item.Model = model;
    }

    private static string DescribeTemperature(double value)
    {
        var qualificatif = value switch
        {
            <= 0.30 => "Précis et fiable",
            <= 0.80 => "Équilibré",
            <= 1.30 => "Créatif",
            _ => "Très créatif",
        };
        return $"{value.ToString("0.00", CultureInfo.CurrentCulture)} · {qualificatif}";
    }

    public void ClearApiKey(string providerKey)
    {
        switch (providerKey)
        {
            case "aistudio":
                GeminiApiKey = string.Empty;
                _clearedCredentials.Add("gemini_api_key");
                GeminiState.SetNeutral("Clé effacée — pensez à enregistrer (Ctrl+S).");
                break;
            case "openai":
                OpenAiApiKey = string.Empty;
                _clearedCredentials.Add("openai_api_key");
                OpenAiState.SetNeutral("Clé effacée — pensez à enregistrer (Ctrl+S).");
                break;
            case "anthropic":
                AnthropicApiKey = string.Empty;
                _clearedCredentials.Add("anthropic_api_key");
                AnthropicState.SetNeutral("Clé effacée — pensez à enregistrer (Ctrl+S).");
                break;
            case "proxy":
                ProxyApiKey = string.Empty;
                _clearedCredentials.Add("proxy_api_key");
                ProxyState.SetNeutral("Clé effacée — pensez à enregistrer (Ctrl+S).");
                break;
            case "vercel":
                VercelApiKey = string.Empty;
                _clearedCredentials.Add("vercel_api_key");
                VercelState.SetNeutral("Clé effacée — pensez à enregistrer (Ctrl+S).");
                break;
        }
    }

    // ─────────────── Tests de connexion ───────────────

    [RelayCommand]
    public async Task TestConnectionAsync(string? providerName)
    {
        var state = StateOf(providerName);
        if (state is null || state.IsTesting) return;

        state.SetPending();

        switch (providerName)
        {
            case "aistudio" when !IsGeminiKeyConfigured:
                state.SetWarning("Clé d'API manquante — test impossible"); return;
            case "openai" when !IsOpenAiKeyConfigured:
                state.SetWarning("Clé d'API manquante — test impossible"); return;
            case "anthropic" when !IsAnthropicKeyConfigured:
                state.SetWarning("Clé d'API manquante — test impossible"); return;
            case "vercel" when !IsVercelKeyConfigured:
                state.SetWarning("Clé d'API manquante — test impossible"); return;
            case "proxy" when string.IsNullOrWhiteSpace(ProxyBaseUrl):
                state.SetWarning("Adresse du serveur requise"); return;
            case "vertex" when string.IsNullOrWhiteSpace(VertexProject):
                state.SetWarning("Identifiant du projet Google Cloud requis"); return;
        }

        if (_connectionTester is null)
        {
            state.SetNeutral("Testeur de connexion indisponible.");
            return;
        }

        var result = await _connectionTester.TestAsync(providerName!);

        if (result.Success)
            state.SetOk(Math.Max(1, result.LatencyMs));
        else
            state.SetError(result.Message);
    }

    [RelayCommand]
    public async Task ScanProxyModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(ProxyBaseUrl))
        {
            ProxyState.SetWarning("Veuillez saisir l'URL du proxy.");
            return;
        }

        if (_proxyScanner is null)
        {
            ProxyState.SetNeutral("Scanner de modèles indisponible.");
            return;
        }

        ProxyState.SetPending();
        try
        {
            var modelList = await _proxyScanner.ScanAsync(ProxyBaseUrl, ProxyApiKey);

            if (modelList.Count > 0)
            {
                ProviderCatalog.UpdateProxyModels(modelList);
                foreach (var item in RoutingMatrix)
                {
                    if (item.SelectedProvider?.Key == "proxy")
                    {
                        item.ApplyCustomProxyModel(modelList[0]);
                    }
                }
                ProxyState.SetOk(120);
                StatusMessage = $"✅ {modelList.Count} modèle(s) détecté(s) sur le proxy : {string.Join(", ", modelList.Take(3))}";
            }
            else
            {
                ProxyState.SetWarning("Réponse reçue mais aucun modèle trouvé.");
            }
        }
        catch (OperationCanceledException)
        {
            ProxyState.SetWarning("Scan annulé.");
        }
        catch (Exception ex)
        {
            ProxyState.SetWarning(ex.Message);
        }
    }

    [RelayCommand]
    private async Task TestAllConnectionsAsync()
    {
        StatusMessage = "Diagnostic des six fournisseurs en cours…";
        foreach (var key in new[] { "aistudio", "openai", "anthropic", "proxy", "vertex", "vercel" })
            await TestConnectionAsync(key);

        var ok = AllStates().Count(s => s.Health == ConnectionHealth.Ok);
        StatusMessage = ok == 6
            ? "✅ Diagnostic terminé : les 6 fournisseurs sont opérationnels."
            : $"Diagnostic terminé : {ok}/6 fournisseurs opérationnels — vérifiez les clés signalées.";
    }

    private ProviderConnectionState? StateOf(string? key) => key switch
    {
        "aistudio" => GeminiState,
        "openai" => OpenAiState,
        "anthropic" => AnthropicState,
        "proxy" => ProxyState,
        "vertex" => VertexState,
        "vercel" => VercelState,
        _ => null,
    };

    private IEnumerable<ProviderConnectionState> AllStates()
        => new[] { GeminiState, OpenAiState, AnthropicState, ProxyState, VertexState, VercelState };
}
