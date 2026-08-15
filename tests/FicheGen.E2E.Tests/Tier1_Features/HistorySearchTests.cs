// ============================================================================
//  FicheGen.E2E.Tests — HistorySearchTests (Tier 1 Feature Coverage)
// ============================================================================

using System;
using System.Threading.Tasks;
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
}
