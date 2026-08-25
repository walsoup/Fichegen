using System.Text.Json;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Storage;

namespace FicheGen.Infrastructure.Storage;

public sealed class SettingsStore : ISettingsStore
{
    private readonly ICredentialStore _credentialStore;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsStore(ICredentialStore credentialStore, string? settingsPath = null)
    {
        _credentialStore = credentialStore;
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FicheGen",
            "settings.json");
    }

    /// <summary>
    /// Un fichier par type de réglages. Historique : tous les types partageaient
    /// <c>settings.json</c>, et chaque sauvegarde de l'état de la coque écrasait
    /// les AppSettings (premier démarrage reposé, thème, profil enseignant perdus).
    /// </summary>
    private string PathFor<T>() where T : class =>
        typeof(T) == typeof(ShellStateSettings)
            ? Path.Combine(Path.GetDirectoryName(_settingsPath)!, "shell-state.json")
            : _settingsPath;

    public T GetSettings<T>() where T : class, new()
    {
        var path = PathFor<T>();
        _semaphore.Wait();
        try
        {
            // Migration : l'état de la coque vivait autrefois dans settings.json.
            if (!File.Exists(path))
            {
                var migrated = TryMigrateLegacyShellState<T>(path);
                if (migrated is not null)
                {
                    return migrated;
                }

                var newSettings = new T();
                if (newSettings is AppSettings appSettings)
                {
                    LoadSecretsInto(appSettings);
                }
                return newSettings;
            }

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();

            if (settings is AppSettings loadedAppSettings)
            {
                LoadSecretsInto(loadedAppSettings);
            }

            return settings;
        }
        catch
        {
            var fallback = new T();
            if (fallback is AppSettings fallbackAppSettings)
            {
                LoadSecretsInto(fallbackAppSettings);
            }
            return fallback;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Récupère l'état de la coque depuis l'ancien fichier partagé, s'il s'y trouve
    /// encore (signature : présence de la propriété « IsAssistantVisible »).</summary>
    private T? TryMigrateLegacyShellState<T>(string newPath) where T : class, new()
    {
        try
        {
            if (typeof(T) != typeof(ShellStateSettings) || !File.Exists(_settingsPath))
            {
                return null;
            }

            var legacyJson = File.ReadAllText(_settingsPath);
            if (!legacyJson.Contains("IsAssistantVisible", StringComparison.Ordinal))
            {
                return null; // settings.json contient de vrais AppSettings : ne pas y toucher.
            }

            var shellState = JsonSerializer.Deserialize<T>(legacyJson, JsonOptions);
            if (shellState is not null)
            {
                // Écrit immédiatement dans le nouveau fichier dédié.
                Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                File.WriteAllText(newPath, legacyJson);
                return shellState;
            }
        }
        catch
        {
            // Migration best-effort : à défaut, valeurs par défaut.
        }
        return null;
    }

    public async Task SaveSettingsAsync<T>(T settings, CancellationToken ct = default) where T : class
    {
        var path = PathFor<T>();
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string jsonToWrite;

            if (settings is AppSettings appSettings)
            {
                // 1. Sync secrets with credential store
                SyncSecret("gemini_api_key", appSettings.Ai.GeminiApiKey);
                SyncSecret("openai_api_key", appSettings.Ai.OpenAiApiKey);
                SyncSecret("anthropic_api_key", appSettings.Ai.AnthropicApiKey);
                SyncSecret("proxy_api_key", appSettings.Ai.ProxyApiKey);
                SyncSecret("vercel_api_key", appSettings.Ai.VercelApiKey);

                // 2. Serialize settings to disk (secrets are decorated with [JsonIgnore])
                jsonToWrite = JsonSerializer.Serialize(appSettings, JsonOptions);
            }
            else
            {
                jsonToWrite = JsonSerializer.Serialize(settings, JsonOptions);
            }

            // Atomic file write pattern: write .tmp -> flush -> Move
            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, jsonToWrite, ct).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void SyncSecret(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            try { _credentialStore.Remove(key); } catch { }
        }
        else
        {
            try { _credentialStore.Set(key, value); } catch { }
        }
    }

    private void LoadSecretsInto(AppSettings appSettings)
    {
        appSettings.Ai.GeminiApiKey = _credentialStore.Get("gemini_api_key") ?? string.Empty;
        appSettings.Ai.OpenAiApiKey = _credentialStore.Get("openai_api_key") ?? string.Empty;
        appSettings.Ai.AnthropicApiKey = _credentialStore.Get("anthropic_api_key") ?? string.Empty;
        appSettings.Ai.ProxyApiKey = _credentialStore.Get("proxy_api_key") ?? string.Empty;
        appSettings.Ai.VercelApiKey = _credentialStore.Get("vercel_api_key") ?? string.Empty;
    }
}
