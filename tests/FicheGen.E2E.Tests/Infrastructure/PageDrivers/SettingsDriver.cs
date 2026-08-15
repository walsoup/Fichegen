// ============================================================================
//  FicheGen.E2E.Tests — SettingsDriver
//  Pilote pour la page de configuration et de clés d'API.
// ============================================================================

using System.Threading.Tasks;
using FicheGen.App.ViewModels;
using FicheGen.Core.Storage;

namespace FicheGen.E2E.Tests.Infrastructure.PageDrivers;

public sealed class SettingsDriver
{
    private readonly TestEnvironment _env;

    public SettingsDriver(TestEnvironment env)
    {
        _env = env;
    }

    public void SelectGlobalProvider(string provider)
    {
        _env.SettingsViewModel.GlobalProvider = provider;
    }

    public void SetApiKey(string provider, string key)
    {
        switch (provider.ToLowerInvariant())
        {
            case "openai": _env.SettingsViewModel.OpenAiApiKey = key; break;
            case "aistudio":
            case "gemini": _env.SettingsViewModel.GeminiApiKey = key; break;
            case "anthropic": _env.SettingsViewModel.AnthropicApiKey = key; break;
            case "proxy": _env.SettingsViewModel.ProxyApiKey = key; break;
        }
    }

    public string GetApiKey(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "openai" => _env.SettingsViewModel.OpenAiApiKey,
            "aistudio" or "gemini" => _env.SettingsViewModel.GeminiApiKey,
            "anthropic" => _env.SettingsViewModel.AnthropicApiKey,
            "proxy" => _env.SettingsViewModel.ProxyApiKey,
            _ => string.Empty
        };
    }

    public void SaveSettings()
    {
        _env.SettingsViewModel.SaveSettingsAsync().GetAwaiter().GetResult();
    }

    public AppSettings GetLoadedSettings()
    {
        return _env.SettingsStore.GetSettings<AppSettings>();
    }
}
