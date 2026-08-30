using System;
using System.Net.Http;
using System.Threading.Tasks;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Storage;
using Windows.Networking.Connectivity;

namespace FicheGen.App.Services;

/// <summary>
/// Implémentation du service d'état de préparation de l'assistant IA (UX-05, UX-34).
/// </summary>
public sealed class ReadinessService : IReadinessService
{
    private readonly ISettingsStore _settingsStore;
    private readonly ICredentialStore _credentialStore;

    private ReadinessState _state = ReadinessState.Unknown;
    private string _activeProviderKey = "cloud";
    private string _activeModelName = "Cloud PROFstudio";
    private string? _lastErrorMessage;

    public event EventHandler? ReadinessChanged;

    public ReadinessState State => _state;
    public string ActiveProviderKey => _activeProviderKey;
    public string ActiveModelName => _activeModelName;
    public string? LastErrorMessage => _lastErrorMessage;

    public bool CanGenerate => _state == ReadinessState.Ready || _state == ReadinessState.Degraded;

    public string ShapeGlyph => _state switch
    {
        ReadinessState.Unknown => "○",
        ReadinessState.NotConfigured => "○",
        ReadinessState.Checking => "◐",
        ReadinessState.Ready => "●",
        ReadinessState.Degraded => "▲",
        ReadinessState.Blocked => "■",
        ReadinessState.Offline => "📡",
        _ => "○"
    };

    public string StatusTitle => _state switch
    {
        ReadinessState.Unknown => "Assistant IA — vérification…",
        ReadinessState.NotConfigured => "Assistant IA — à configurer",
        ReadinessState.Checking => "Assistant IA — test en cours…",
        ReadinessState.Ready => "Assistant IA — prêt",
        ReadinessState.Degraded => "Assistant IA — connexion instable",
        ReadinessState.Blocked => "Assistant IA — clé refusée",
        ReadinessState.Offline => "Assistant IA — hors ligne",
        _ => "Assistant IA"
    };

    public string StatusDetails => _state switch
    {
        ReadinessState.Unknown => "Vérification de la configuration de l'assistant…",
        ReadinessState.NotConfigured => "Aucune clé de connexion enregistrée. Cliquez pour configurer votre assistant.",
        ReadinessState.Checking => "Test de la connexion au service IA en cours…",
        ReadinessState.Ready => $"Fournisseur : {_activeProviderKey} · Modèle : {_activeModelName}\nCliquez pour gérer les paramètres.",
        ReadinessState.Degraded => string.IsNullOrWhiteSpace(_lastErrorMessage)
            ? "Difficulté réseau temporaire. Cliquez pour réessayer."
            : $"{_lastErrorMessage}\nCliquez pour réessayer.",
        ReadinessState.Blocked => string.IsNullOrWhiteSpace(_lastErrorMessage)
            ? "Clé de connexion invalide ou expirée. Cliquez pour mettre à jour votre clé."
            : $"{_lastErrorMessage}\nCliquez pour corriger votre clé dans les Paramètres.",
        ReadinessState.Offline => "Aucune connexion Internet active détectée sur votre ordinateur. Vos documents existants restent accessibles.",
        _ => "Statut de l'assistant IA"
    };

    public ReadinessService(ISettingsStore settingsStore, ICredentialStore credentialStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
    }

    public async Task RefreshAsync()
    {
        try
        {
            var settings = _settingsStore.GetSettings<AppSettings>();
            _activeProviderKey = string.IsNullOrWhiteSpace(settings.Ai.GlobalProvider) ? "cloud" : settings.Ai.GlobalProvider.ToLowerInvariant();
            
            if (settings.Ai.Models.TryGetValue(_activeProviderKey, out var model) && !string.IsNullOrWhiteSpace(model))
            {
                _activeModelName = model;
            }
            else
            {
                _activeModelName = _activeProviderKey == "cloud" ? "Cloud PROFstudio" : "Par défaut";
            }

            // Vérification connexion Internet (UX-34)
            if (_activeProviderKey != "proxy")
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                var isOnline = profile != null && profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
                if (!isOnline)
                {
                    SetState(ReadinessState.Offline);
                    return;
                }
            }

            // Vérification des identifiants dans le coffre
            if (_activeProviderKey == "proxy")
            {
                SetState(ReadinessState.Ready);
                return;
            }

            var key = GetApiKeyForProvider(_activeProviderKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                SetState(ReadinessState.NotConfigured);
            }
            else
            {
                _lastErrorMessage = null;
                SetState(ReadinessState.Ready);
            }
        }
        catch
        {
            SetState(ReadinessState.NotConfigured);
        }

        await Task.CompletedTask;
    }

    public void ReportExecutionOutcome(bool success, Exception? error = null)
    {
        if (success)
        {
            _lastErrorMessage = null;
            SetState(ReadinessState.Ready);
            return;
        }

        if (error != null)
        {
            _lastErrorMessage = ErrorMessageTranslator.ToUserFriendlyMessage(error);

            if (error is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                if (code == 401 || code == 403 || code == 400)
                {
                    SetState(ReadinessState.Blocked);
                    return;
                }
                if (code == 429 || code >= 500)
                {
                    SetState(ReadinessState.Degraded);
                    return;
                }
            }

            var msg = error.Message;
            if (msg.Contains("401", StringComparison.Ordinal) || msg.Contains("403", StringComparison.Ordinal) ||
                msg.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) || msg.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase))
            {
                SetState(ReadinessState.Blocked);
                return;
            }

            SetState(ReadinessState.Degraded);
        }
        else
        {
            SetState(ReadinessState.Degraded);
        }
    }

    private string? GetApiKeyForProvider(string provider)
    {
        return provider switch
        {
            "cloud" or "profstudio" => _credentialStore.Get("supabase_access_token"),
            "aistudio" or "gemini" => _credentialStore.Get("gemini_api_key") ?? _credentialStore.Get("aistudio_api_key"),
            "openai" => _credentialStore.Get("openai_api_key"),
            "anthropic" => _credentialStore.Get("anthropic_api_key"),
            "deepseek" => _credentialStore.Get("deepseek_api_key"),
            "groq" => _credentialStore.Get("groq_api_key"),
            "mistral" => _credentialStore.Get("mistral_api_key"),
            "openrouter" => _credentialStore.Get("openrouter_api_key"),
            "vertex" => _credentialStore.Get("vertex_service_account") ?? _credentialStore.Get("vertex_api_key"),
            "vercel" => _credentialStore.Get("vercel_api_key"),
            "proxy" => "local",
            _ => _credentialStore.Get($"{provider}_api_key")
        };
    }

    private void SetState(ReadinessState newState)
    {
        if (_state != newState)
        {
            _state = newState;
            ReadinessChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
