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
                // 1. Save secrets to credential store
                if (!string.IsNullOrEmpty(appSettings.Ai.GeminiApiKey))
                    _credentialStore.Set("gemini_api_key", appSettings.Ai.GeminiApiKey);
                if (!string.IsNullOrEmpty(appSettings.Ai.ProxyApiKey))
                    _credentialStore.Set("proxy_api_key", appSettings.Ai.ProxyApiKey);
                if (!string.IsNullOrEmpty(appSettings.Ai.VercelApiKey))
                    _credentialStore.Set("vercel_api_key", appSettings.Ai.VercelApiKey);

                // 2. Clone/snapshot settings and strip secrets before disk serialization
                var tempGemini = appSettings.Ai.GeminiApiKey;
                var tempProxy = appSettings.Ai.ProxyApiKey;
                var tempVercel = appSettings.Ai.VercelApiKey;

                try
                {
                    appSettings.Ai.GeminiApiKey = string.Empty;
                    appSettings.Ai.ProxyApiKey = string.Empty;
                    appSettings.Ai.VercelApiKey = string.Empty;

                    jsonToWrite = JsonSerializer.Serialize(appSettings, JsonOptions);
                }
                finally
                {
                    // Restore in-memory values for UI binding
                    appSettings.Ai.GeminiApiKey = tempGemini;
                    appSettings.Ai.ProxyApiKey = tempProxy;
                    appSettings.Ai.VercelApiKey = tempVercel;
                }
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

    private void LoadSecretsInto(AppSettings appSettings)
    {
        appSettings.Ai.GeminiApiKey = _credentialStore.Get("gemini_api_key") ?? string.Empty;
        appSettings.Ai.ProxyApiKey = _credentialStore.Get("proxy_api_key") ?? string.Empty;
        appSettings.Ai.VercelApiKey = _credentialStore.Get("vercel_api_key") ?? string.Empty;
    }
}
