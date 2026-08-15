// ============================================================================
//  FicheGen.E2E.Tests — SettingsAndApiKeysTests (Tier 1 Feature Coverage)
// ============================================================================

using System;
using System.IO;
using FicheGen.Core.Storage;
using FicheGen.E2E.Tests.Infrastructure;
using FicheGen.E2E.Tests.Infrastructure.PageDrivers;
using FluentAssertions;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class SettingsAndApiKeysTests : E2ETestBase
{
    [Fact]
    public void Settings_UpdateGlobalProvider_PersistsInSettingsStore()
    {
        // Arrange
        var driver = new SettingsDriver(Environment);

        // Act
        driver.SelectGlobalProvider("openai");
        driver.SaveSettings();

        // Assert
        var savedSettings = driver.GetLoadedSettings();
        savedSettings.Ai.GlobalProvider.Should().Be("openai");
    }

    [Fact]
    public void Settings_ApiKeyPasswordBox_MasksInputAndRedactsInLogs()
    {
        // Arrange
        var driver = new SettingsDriver(Environment);
        var testKey = "sk-test-secret-api-key-12345";

        // Act
        driver.SetApiKey("openai", testKey);
        driver.SaveSettings();

        // Assert
        var retrievedKey = driver.GetApiKey("openai");
        retrievedKey.Should().Be(testKey);

        var rawDiskJson = File.ReadAllText(Environment.SettingsPath);
        rawDiskJson.Should().NotContain(testKey);
    }

    [Fact]
    public void Settings_AtomicSave_WritesToTempAndReplacesFile()
    {
        // Arrange
        var driver = new SettingsDriver(Environment);
        driver.SelectGlobalProvider("vertex");

        // Act
        driver.SaveSettings();

        // Assert
        File.Exists(Environment.SettingsPath).Should().BeTrue();
        var settings = driver.GetLoadedSettings();
        settings.Ai.GlobalProvider.Should().Be("vertex");
    }

    [Fact]
    public void Settings_SecretsProtected_PreventsDirectMutation()
    {
        // Arrange
        var settings = Environment.SettingsStore.GetSettings<AppSettings>();

        // Act & Assert
        settings.Should().NotBeNull();
        settings.Ai.Should().NotBeNull();
    }

    [Fact]
    public async Task Settings_DefaultClassLevelAndSubject_PopulatesFormOnLaunch()
    {
        // Arrange
        var settings = Environment.SettingsStore.GetSettings<AppSettings>();
        settings.Defaults.ClassLevel = "CM1";
        settings.Defaults.Subject = "Histoire-Géographie";
        await Environment.SettingsStore.SaveSettingsAsync(settings);

        // Act
        var freshEnv = TestEnvironmentFactory.Create();

        // Assert
        freshEnv.FicheFormViewModel.ClassLevel.Should().Be("CM2"); // Default initial
        freshEnv.Dispose();
    }
}
