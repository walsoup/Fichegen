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

    [Fact]
    public async Task SaveShellState_DoesNotOverwriteAppSettings()
    {
        // Arrange — l'historique : les deux types partageaient un fichier unique,
        // et chaque sauvegarde de la coque écrasait les AppSettings.
        var appSettings = new AppSettings
        {
            IsFirstRunCompleted = true,
            Defaults = { TeacherName = "Mme Martin", ClassLevel = "CE2" },
            Ui = { Theme = "dark", AccentColor = "#059669" }
        };
        await _settingsStore.SaveSettingsAsync(appSettings);

        var shellState = new ShellStateSettings
        {
            X = 10, Y = 20, Width = 800, Height = 600,
            IsAssistantVisible = true,
            Theme = "Dark",
            LastNavigationTag = "QuizPage"
        };

        // Act
        await _settingsStore.SaveSettingsAsync(shellState);

        // Assert — les AppSettings ont survécu à la sauvegarde de la coque.
        var reloadedApp = _settingsStore.GetSettings<AppSettings>();
        reloadedApp.IsFirstRunCompleted.Should().BeTrue();
        reloadedApp.Defaults.TeacherName.Should().Be("Mme Martin");
        reloadedApp.Defaults.ClassLevel.Should().Be("CE2");
        reloadedApp.Ui.Theme.Should().Be("dark");
        reloadedApp.Ui.AccentColor.Should().Be("#059669");

        var reloadedShell = _settingsStore.GetSettings<ShellStateSettings>();
        reloadedShell.IsAssistantVisible.Should().BeTrue();
        reloadedShell.Theme.Should().Be("Dark");
        reloadedShell.LastNavigationTag.Should().Be("QuizPage");
    }

    [Fact]
    public void GetShellState_MigratesLegacySharedFile()
    {
        // Arrange — ancien fichier partagé contenant l'état de la coque.
        var legacyJson = """
            {
              "X": 15,
              "Y": 25,
              "Width": 1024,
              "Height": 768,
              "IsMaximized": true,
              "IsAssistantVisible": false,
              "Theme": "System",
              "LastNavigationTag": "HistoryPage"
            }
            """;
        Directory.CreateDirectory(_testDir);
        File.WriteAllText(_tempPath, legacyJson);

        // Act
        var shellState = _settingsStore.GetSettings<ShellStateSettings>();

        // Assert — valeurs migrées + nouveau fichier dédié créé.
        shellState.IsAssistantVisible.Should().BeFalse();
        shellState.Theme.Should().Be("System");
        shellState.LastNavigationTag.Should().Be("HistoryPage");
        File.Exists(Path.Combine(_testDir, "shell-state.json")).Should().BeTrue();

        // Les AppSettings ne doivent PAS absorber le JSON de la coque :
        var appSettings = _settingsStore.GetSettings<AppSettings>();
        appSettings.IsFirstRunCompleted.Should().BeFalse(); // valeur par défaut propre
    }
}
