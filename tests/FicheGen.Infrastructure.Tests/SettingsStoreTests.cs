using FicheGen.Core.Abstractions;
using FicheGen.Core.Storage;
using FicheGen.Infrastructure.Security;
using FicheGen.Infrastructure.Storage;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _tempPath;
    private readonly DpapiCredentialStore _credStore;
    private readonly SettingsStore _settingsStore;

    public SettingsStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"fichegen_test_{Guid.NewGuid():N}");
        _tempPath = Path.Combine(_testDir, "settings.json");
        _credStore = new DpapiCredentialStore(Path.Combine(_testDir, "credentials"));
        _settingsStore = new SettingsStore(_credStore, _tempPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SaveSettings_ShouldStripSecretsFromDiskAndStoreInVault()
    {
        // Arrange
        var settings = new AppSettings();
        settings.Ai.GlobalProvider = "aistudio";
        settings.Ai.GeminiApiKey = "SECRET_KEY_12345_XYZ";

        // Act
        await _settingsStore.SaveSettingsAsync(settings);

        // Assert
        File.Exists(_tempPath).Should().BeTrue();
        var rawDiskJson = await File.ReadAllTextAsync(_tempPath);

        // 1. Raw disk JSON must NOT contain the secret string
        rawDiskJson.Should().NotContain("SECRET_KEY_12345_XYZ");

        // 2. Credential store must contain the secret
        var storedSecret = _credStore.Get("gemini_api_key");
        storedSecret.Should().Be("SECRET_KEY_12345_XYZ");

        // 3. Reloading settings re-hydrates the secret for in-memory use
        var reloaded = _settingsStore.GetSettings<AppSettings>();
        reloaded.Ai.GeminiApiKey.Should().Be("SECRET_KEY_12345_XYZ");
    }
}
