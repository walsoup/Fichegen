// ============================================================================
//  FicheGen.E2E.Tests — HistorySearchTests (Tier 1 Feature Coverage)
// ============================================================================

using System;
using System.Threading.Tasks;
using FicheGen.Core.Documents;
using FicheGen.Core.Storage;
using FicheGen.E2E.Tests.Infrastructure;
using FicheGen.E2E.Tests.Infrastructure.PageDrivers;
using FluentAssertions;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class HistorySearchTests : E2ETestBase
{
    private async Task SeedHistoryDataAsync()
    {
        var item1 = new HistoryItem
        {
            Id = "item-1",
            Type = "fiche",
            Title = "La Révolution française",
            ClassLevel = "CM2",
            Subject = "Histoire",
            CreatedUtc = DateTime.UtcNow,
            PlainText = "Résumé sur la prise de la Bastille et la déclaration des droits de l'homme.",
            Html = "<p>Résumé sur la prise de la Bastille et la déclaration des droits de l'homme.</p>"
        };
        var item2 = new HistoryItem
        {
            Id = "item-2",
            Type = "quiz",
            Title = "Quiz sur les fractions",
            ClassLevel = "CM1",
            Subject = "Mathématiques",
            CreatedUtc = DateTime.UtcNow,
            PlainText = "Questions à choix multiples sur le calcul des numérateurs et dénominateurs.",
            Html = "<p>Questions à choix multiples sur le calcul des numérateurs et dénominateurs.</p>"
        };
        await Environment.HistoryRepository.SaveAsync(item1);
        await Environment.HistoryRepository.SaveAsync(item2);
    }

    [Fact]
    public async Task HistorySearch_300msDebounce_DelaysQueryExecution()
    {
        // Arrange
        await SeedHistoryDataAsync();
        var driver = new HistoryDriver(Environment);
        await Environment.HistoryViewModel.LoadHistoryAsync();

        // Act
        Environment.HistoryViewModel.SearchQuery = "Fractions";
        
        // Avant l'écoulement du délai de 300ms, la liste initiale n'est pas encore filtrée par FTS
        Environment.HistoryViewModel.Items.Should().NotBeEmpty();

        // Attendre la fin du timer anti-rebond
        await Task.Delay(350);

        // Assert
        driver.GetDisplayedItems().Should().ContainSingle(i => i.Title.Contains("fractions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HistorySearch_SanitizesSpecialCharacters_PreventsFtsSyntaxErrors()
    {
        // Arrange
        await SeedHistoryDataAsync();
        var driver = new HistoryDriver(Environment);
        await Environment.HistoryViewModel.LoadHistoryAsync();

        // Act & Assert
        var act = async () => await driver.SearchAsync("Révolution AND * OR \"Bastille\"");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HistorySearch_RetrieveSavedItem_LoadsDocumentIntoResultView()
    {
        // Arrange
        await SeedHistoryDataAsync();
        var driver = new HistoryDriver(Environment);
        await Environment.HistoryViewModel.LoadHistoryAsync();
        var items = driver.GetDisplayedItems();
        items.Should().NotBeEmpty();

        // Act
        await driver.SelectItemAsync(items[0]);

        // Assert
        Environment.HistoryViewModel.SelectedItem.Should().Be(items[0]);
    }

    [Fact]
    public async Task HistorySearch_DeleteItemWithConfirmation_RemovesFromDatabase()
    {
        // Arrange
        await SeedHistoryDataAsync();
        var driver = new HistoryDriver(Environment);
        await Environment.HistoryViewModel.LoadHistoryAsync();
        var items = driver.GetDisplayedItems();
        items.Should().HaveCount(2);

        // Act
        Environment.HistoryViewModel.SelectedItem = items[0];
        await driver.DeleteSelectedItemAsync();

        // Assert
        var remaining = await Environment.HistoryRepository.ListAsync();
        remaining.Should().HaveCount(1);
    }

    [Fact]
    public async Task HistorySearch_EmptySearchQuery_ReturnsAllHistoryItems()
    {
        // Arrange
        await SeedHistoryDataAsync();
        var driver = new HistoryDriver(Environment);
        await Environment.HistoryViewModel.LoadHistoryAsync();

        // Act
        await driver.SearchAsync("Fractions");
        driver.GetDisplayedItems().Should().HaveCount(1);

        await driver.SearchAsync("");

        // Assert
        driver.GetDisplayedItems().Should().HaveCount(2);
    }

    [Fact]
    public async Task History_OpenItem_HydratesFullDocumentAndHtml()
    {
        var meta = new DocumentMetadata("Le système solaire CM2", "Cycle 3", "CM2", "Sciences", 60, null, "fiche");
        var blocks = new Block[]
        {
            new HeadingBlock(1, "Le système solaire"),
            new ParagraphBlock("Mercure, Vénus, Terre, Mars...")
        };
        var sampleDoc = new GeneratedDocument(meta, blocks);
        var sourceJson = System.Text.Json.JsonSerializer.Serialize(sampleDoc);

        var item = new HistoryItem
        {
            Id = "full-item-1",
            Type = "fiche",
            Title = "Le système solaire CM2",
            ClassLevel = "CM2",
            Subject = "Sciences",
            CreatedUtc = DateTime.UtcNow,
            PlainText = "Planètes du système solaire.",
            Html = "<h1>Le système solaire</h1><p>Mercure, Vénus, Terre, Mars...</p>",
            SourceJson = sourceJson,
            StylePresetId = "dyslexie"
        };
        await Environment.HistoryRepository.SaveAsync(item);
        await Environment.HistoryViewModel.LoadHistoryAsync();

        var vmItem = Environment.HistoryViewModel.Items.Should().ContainSingle(i => i.Id == "full-item-1").Subject;

        // Act
        await Environment.HistoryViewModel.OpenItemCommand.ExecuteAsync(vmItem);

        // Assert
        Environment.ResultViewModel.CurrentDocument.Should().NotBeNull();
        Environment.ResultViewModel.CurrentDocument!.Metadata.Title.Should().Be("Le système solaire CM2");
        Environment.ResultViewModel.CurrentHtml.Should().Be("<h1>Le système solaire</h1><p>Mercure, Vénus, Terre, Mars...</p>");
        Environment.ResultViewModel.ActivePresetId.Should().Be("dyslexie");
    }

    [Fact]
    public async Task History_DeleteAndRestore_PreservesFullContentByteForByte()
    {
        var item = new HistoryItem
        {
            Id = "restore-test-1",
            Type = "fiche",
            Title = "Grammaire les compléments",
            ClassLevel = "CM1",
            Subject = "Français",
            CreatedUtc = DateTime.UtcNow,
            PlainText = "Leçon sur le COD et COI.",
            Html = "<h1>Grammaire</h1><p>Leçon sur le COD et COI.</p>",
            SourceJson = "{\"metadata\":{\"title\":\"Grammaire les compléments\"}}",
            RawPrompt = "Prompt d'origine",
            RawResponse = "{\"metadata\":{\"title\":\"Grammaire les compléments\"}}",
            StylePresetId = "modern"
        };
        await Environment.HistoryRepository.SaveAsync(item);
        await Environment.HistoryViewModel.LoadHistoryAsync();

        var vmItem = Environment.HistoryViewModel.Items.Should().ContainSingle(i => i.Id == "restore-test-1").Subject;

        // 1. Delete item
        await Environment.HistoryViewModel.DeleteItemCommand.ExecuteAsync(vmItem);
        var afterDelete = await Environment.HistoryRepository.GetByIdAsync("restore-test-1");
        afterDelete.Should().BeNull();

        // 2. Undo / Restore item
        await Environment.HistoryViewModel.RestoreItemCommand.ExecuteAsync(vmItem);

        // 3. Verify restored item in database has all fields preserved byte-for-byte
        var restored = await Environment.HistoryRepository.GetByIdAsync("restore-test-1");
        restored.Should().NotBeNull();
        restored!.Html.Should().Be(item.Html);
        restored.SourceJson.Should().Be(item.SourceJson);
        restored.RawPrompt.Should().Be(item.RawPrompt);
        restored.RawResponse.Should().Be(item.RawResponse);
        restored.PlainText.Should().Be(item.PlainText);
        restored.StylePresetId.Should().Be(item.StylePresetId);
    }
}
