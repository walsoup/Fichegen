// ============================================================================
//  FicheGen.E2E.Tests — UxDialogsAndEmptyStatesTests (Tier 1 Feature Coverage)
// ============================================================================

using System.Threading.Tasks;
using FicheGen.App.ViewModels;
using FicheGen.E2E.Tests.Infrastructure;
using FicheGen.E2E.Tests.Infrastructure.PageDrivers;
using FluentAssertions;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class UxDialogsAndEmptyStatesTests : E2ETestBase
{
    [Fact]
    public void Ux_FirstRunDialog_InitialStateHandling()
    {
        // Arrange & Act
        var hasRestored = Environment.FicheFormViewModel.HasRestoredDraft;

        // Assert
        hasRestored.Should().BeFalse();
    }

    [Fact]
    public async Task Ux_EmptyHistory_DisplaysEmptyState()
    {
        // Arrange
        await Environment.HistoryViewModel.LoadHistoryAsync();

        // Act
        var items = Environment.HistoryViewModel.Items;

        // Assert
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Ux_FormValidation_DisplaysInfoBarWarning()
    {
        // Arrange
        var driver = new FicheFormDriver(Environment);
        driver.FillFicheForm(classLevel: "CM2", subject: "Mathématiques", topic: "x", durationMinutes: 60);

        // Act
        await driver.TriggerGenerationAsync();

        // Assert
        driver.StatusSeverity.Should().Be(StatusSeverity.Warning);
        driver.StatusMessage.Should().Contain("⚠️");
    }

    [Fact]
    public async Task Ux_ThemeSwitching_UpdatesSettingsTheme()
    {
        // Arrange
        var settingsVm = Environment.SettingsViewModel;

        // Act
        settingsVm.Theme = "dark";
        await settingsVm.SaveSettingsAsync();

        // Assert
        var updatedSettings = Environment.SettingsStore.GetSettings<FicheGen.Core.Storage.AppSettings>();
        updatedSettings.Ui.Theme.Should().Be("dark");
    }

    [Fact]
    public async Task Ux_ResetForm_ClearsFieldsAndRestoresStatus()
    {
        // Arrange
        var driver = new FicheFormDriver(Environment);
        driver.FillFicheForm(classLevel: "CM2", subject: "Mathématiques", topic: "Sujet de test", durationMinutes: 90);

        // Act
        driver.ResetForm();

        // Assert
        Environment.FicheFormViewModel.Topic.Should().BeEmpty();
        Environment.FicheFormViewModel.DurationMinutes.Should().Be(60);
        driver.StatusMessage.Should().Contain("Formulaire réinitialisé");
    }
}
