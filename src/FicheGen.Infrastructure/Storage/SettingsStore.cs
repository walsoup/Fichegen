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

    public T GetSettings<T>() where T : class, new()
    {
        _semaphore.Wait();
        try
        {
            if (!File.Exists(_settingsPath))
            {
                var newSettings = new T();
                if (newSettings is AppSettings appSettings)
                {
                    LoadSecretsInto(appSettings);
                }
                return newSettings;
            }

            var json = File.ReadAllText(_settingsPath);
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

    public async Task SaveSettingsAsync<T>(T settings, CancellationToken ct = default) where T : class
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

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
            var tempPath = _settingsPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, jsonToWrite, ct).ConfigureAwait(false);
            File.Move(tempPath, _settingsPath, overwrite: true);
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
