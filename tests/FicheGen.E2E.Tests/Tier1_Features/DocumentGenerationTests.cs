// ============================================================================
//  FicheGen.E2E.Tests — DocumentGenerationTests (Tier 1 Feature Coverage)
// ============================================================================

using System.Threading.Tasks;
using FicheGen.App.ViewModels;
using FicheGen.E2E.Tests.Infrastructure;
using FicheGen.E2E.Tests.Infrastructure.PageDrivers;
using FluentAssertions;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class DocumentGenerationTests : E2ETestBase
{
    [Fact]
    public async Task GenerateFiche_ValidParameters_ProducesGeneratedDocumentAndHtmlPreview()
    {
        // Arrange
        var driver = new FicheFormDriver(Environment);
        driver.FillFicheForm(classLevel: "CM2", subject: "Mathématiques", topic: "Les fractions simples", durationMinutes: 60);

        // Act
        await driver.TriggerGenerationAsync();

        // Assert
        driver.CurrentDocument.Should().NotBeNull();
        driver.CurrentDocument!.Metadata.Title.Should().NotBeNullOrEmpty();
        driver.CurrentDocument.Metadata.ClassLevel.Should().Be("CM2");
        driver.CurrentPreviewHtml.Should().Contain("Fiche de test pédagogique");
        driver.IsGenerating.Should().BeFalse();
        driver.StatusSeverity.Should().Be(StatusSeverity.Success);
    }

    [Fact]
    public async Task GenerateFiche_MissingRequiredTopic_DisplaysValidationWarning()
    {
        // Arrange
        var driver = new FicheFormDriver(Environment);
        driver.FillFicheForm(classLevel: "CM2", subject: "Mathématiques", topic: "", durationMinutes: 60);

        // Act
        await driver.TriggerGenerationAsync();

        // Assert
        driver.CurrentDocument.Should().BeNull();
        driver.StatusSeverity.Should().Be(StatusSeverity.Warning);
        driver.StatusMessage.Should().Contain("Le sujet de la leçon est obligatoire");
    }

    [Fact]
    public async Task GenerateFiche_TopicLengthUnderMinimum_DisplaysValidationError()
    {
        // Arrange
        var driver = new FicheFormDriver(Environment);
        driver.FillFicheForm(classLevel: "CM2", subject: "Mathématiques", topic: "Ab", durationMinutes: 60);

        // Act
        await driver.TriggerGenerationAsync();

        // Assert
        driver.CurrentDocument.Should().BeNull();
        driver.StatusSeverity.Should().Be(StatusSeverity.Warning);
        driver.StatusMessage.Should().Contain("au moins 3 caractères");
    }

    [Fact]
    public async Task GenerateFiche_DurationOutOfRange_DisplaysValidationError()
    {
        // Arrange
        var driver = new FicheFormDriver(Environment);
        driver.FillFicheForm(classLevel: "CM2", subject: "Mathématiques", topic: "La géométrie", durationMinutes: 300);

        // Act
        await driver.TriggerGenerationAsync();

        // Assert
        driver.CurrentDocument.Should().BeNull();
        driver.StatusSeverity.Should().Be(StatusSeverity.Warning);
        driver.StatusMessage.Should().Contain("comprise entre 5 et 240 minutes");
    }

    [Fact]
    public async Task GenerateFiche_SuccessfulExecution_SavesItemToHistoryRepository()
    {
        // Arrange
        var driver = new FicheFormDriver(Environment);
        driver.FillFicheForm(classLevel: "6e", subject: "Français", topic: "La grammaire et le sujet", durationMinutes: 45);

        // Act
        await driver.TriggerGenerationAsync();

        // Assert
        var history = await Environment.HistoryRepository.ListAsync();
        history.Should().NotBeEmpty();
        history.Should().Contain(item => item.Type == "fiche" && item.ClassLevel == "6e");
    }
}
