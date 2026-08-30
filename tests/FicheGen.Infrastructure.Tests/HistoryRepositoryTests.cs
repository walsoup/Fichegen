using FicheGen.Core.Storage;
using FicheGen.Infrastructure.Storage;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public sealed class HistoryRepositoryTests : IDisposable
{
    private readonly string _tempDbPath;

    public HistoryRepositoryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"fichegen_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesTablesAndFtsIndex()
    {
        var repo = new HistoryRepository(_tempDbPath);
        await repo.InitializeAsync();

        File.Exists(_tempDbPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_And_GetByIdAsync_WorkCorrectly()
    {
        var repo = new HistoryRepository(_tempDbPath);
        var item = new HistoryItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "fiche",
            Title = "Fractions CM2",
            ClassLevel = "CM2",
            Subject = "Mathématiques",
            CreatedUtc = DateTime.UtcNow,
            IsFavorite = false,
            PlainText = "Leçon sur les fractions en classe de CM2",
            Html = "<h1>Fractions</h1><p>Leçon sur les fractions</p>",
            StylePresetId = "modern",
            RawPrompt = "[SYSTEM]\nTu es un assistant\n\n[USER]\nCrée une fiche fractions",
            RawResponse = "{\"metadata\":{\"title\":\"Fractions CM2\"}}"
        };

        await repo.SaveAsync(item);
        var retrieved = await repo.GetByIdAsync(item.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("Fractions CM2");
        retrieved.Type.Should().Be("fiche");
        retrieved.ClassLevel.Should().Be("CM2");
        retrieved.Subject.Should().Be("Mathématiques");
        retrieved.PlainText.Should().Be("Leçon sur les fractions en classe de CM2");
        retrieved.RawPrompt.Should().Be("[SYSTEM]\nTu es un assistant\n\n[USER]\nCrée une fiche fractions");
        retrieved.RawResponse.Should().Be("{\"metadata\":{\"title\":\"Fractions CM2\"}}");
    }

    [Fact]
    public async Task Fts5Search_FindsMatchingDocuments()
    {
        var repo = new HistoryRepository(_tempDbPath);

        var item1 = new HistoryItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "fiche",
            Title = "Géométrie Triangles",
            ClassLevel = "CE2",
            Subject = "Maths",
            PlainText = "Apprendre les propriétés des triangles isocèles et équilatéraux",
            Html = "<p>Triangles</p>"
        };

        var item2 = new HistoryItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "evaluation",
            Title = "Bilan de Grammaire",
            ClassLevel = "CM1",
            Subject = "Français",
            PlainText = "Évaluation sur le sujet du verbe et les compléments d'objet",
            Html = "<p>Grammaire</p>"
        };

        await repo.SaveAsync(item1);
        await repo.SaveAsync(item2);

        var searchResults = await repo.SearchAsync("triangles");
        searchResults.Should().HaveCount(1);
        searchResults[0].Title.Should().Be("Géométrie Triangles");

        var searchGrammaire = await repo.SearchAsync("Grammaire");
        searchGrammaire.Should().HaveCount(1);
        searchGrammaire[0].Title.Should().Be("Bilan de Grammaire");
    }

    [Fact]
    public async Task ToggleFavorite_UpdatesFavoriteState()
    {
        var repo = new HistoryRepository(_tempDbPath);
        var item = new HistoryItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "quiz",
            Title = "Quiz Histoire",
            PlainText = "Quiz rapide sur la Révolution française",
            Html = "<p>Quiz</p>"
        };

        await repo.SaveAsync(item);
        await repo.ToggleFavoriteAsync(item.Id, true);

        var retrieved = await repo.GetByIdAsync(item.Id);
        retrieved!.IsFavorite.Should().BeTrue();

        var favoritesOnly = await repo.SearchAsync(isFavoriteOnly: true);
        favoritesOnly.Should().HaveCount(1);
        favoritesOnly[0].Id.Should().Be(item.Id);
    }

    [Fact]
    public async Task CleanupRetentionAsync_DeletesOldNonFavoriteItems_PreservesFavorites()
    {
        var repo = new HistoryRepository(_tempDbPath);

        var oldNonFav = new HistoryItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "fiche",
            Title = "Ancienne fiche",
            CreatedUtc = DateTime.UtcNow.AddDays(-40),
            IsFavorite = false,
            PlainText = "Ancienne fiche non favorite",
            Html = "<p>Old</p>"
        };

        var oldFav = new HistoryItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "fiche",
            Title = "Ancienne fiche favorite",
            CreatedUtc = DateTime.UtcNow.AddDays(-40),
            IsFavorite = true,
            PlainText = "Ancienne fiche favorite à conserver",
            Html = "<p>Old Fav</p>"
        };

        var recentNonFav = new HistoryItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "fiche",
            Title = "Fiche récente",
            CreatedUtc = DateTime.UtcNow.AddDays(-5),
            IsFavorite = false,
            PlainText = "Fiche récente non favorite",
            Html = "<p>Recent</p>"
        };

        await repo.SaveAsync(oldNonFav);
        await repo.SaveAsync(oldFav);
        await repo.SaveAsync(recentNonFav);

        var deletedCount = await repo.CleanupRetentionAsync(30);
        deletedCount.Should().Be(1);

        var remaining = await repo.SearchAsync();
        remaining.Should().HaveCount(2);
        remaining.Select(x => x.Id).Should().Contain(new[] { oldFav.Id, recentNonFav.Id });
        remaining.Select(x => x.Id).Should().NotContain(oldNonFav.Id);
    }

    [Theory]
    [InlineData("l'accord (sujet-verbe) :")]
    [InlineData("AND OR NOT")]
    [InlineData("\"unmatched quotes")]
    [InlineData(":::***???")]
    public async Task SearchAsync_WithSpecialCharactersAndOperators_DoesNotThrow(string query)
    {
        var repo = new HistoryRepository(_tempDbPath);
        var item = new HistoryItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "fiche",
            Title = "L'accord sujet verbe",
            PlainText = "Exercices sur l'accord du participe passé",
            Html = "<p>Accord</p>"
        };

        await repo.SaveAsync(item);

        var act = async () => await repo.SearchAsync(query);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RenameAsync_UpdatesTitleSuccessfully()
    {
        var repo = new HistoryRepository(_tempDbPath);
        var item = new HistoryItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "fiche",
            Title = "Ancien Nom",
            PlainText = "Texte",
            Html = "<p>Texte</p>"
        };

        await repo.SaveAsync(item);
        await repo.RenameAsync(item.Id, "Nouveau Nom");

        var retrieved = await repo.GetByIdAsync(item.Id);
        retrieved!.Title.Should().Be("Nouveau Nom");
    }

    [Fact]
    public async Task SaveAndSearchAndRestore_PreservesAllFieldsByteForByte_WhenHydratedAndRestored()
    {
        var repo = new HistoryRepository(_tempDbPath);
        await repo.InitializeAsync();

        var original = new HistoryItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "fiche",
            Title = "Le cycle de l'eau CM1",
            ClassLevel = "CM1",
            Subject = "Sciences",
            CreatedUtc = DateTime.UtcNow,
            IsFavorite = true,
            PlainText = "Le cycle de l'eau comprend l'évaporation, la condensation et les précipitations.",
            Html = "<article><h1>Le cycle de l'eau</h1><p>Evaporation, condensation, precipitations.</p></article>",
            SourceJson = "{\"title\":\"Le cycle de l'eau\",\"sections\":[{\"heading\":\"Evaporation\"}]}",
            StylePresetId = "dyslexie",
            RawPrompt = "[SYSTEM] Expert enseignant\n[USER] Fiche cycle eau",
            RawResponse = "{\"title\":\"Le cycle de l'eau\"}"
        };

        // 1. Save original
        await repo.SaveAsync(original);

        // 2. Search returns search rows with valid plain text and ID
        var searchResults = await repo.SearchAsync("cycle");
        searchResults.Should().HaveCount(1);
        var found = searchResults[0];
        found.Id.Should().Be(original.Id);
        found.PlainText.Should().Be(original.PlainText);

        // 3. Hydrate via GetByIdAsync
        var hydrated = await repo.GetByIdAsync(found.Id);
        hydrated.Should().NotBeNull();
        hydrated!.Html.Should().Be(original.Html);
        hydrated.SourceJson.Should().Be(original.SourceJson);
        hydrated.RawPrompt.Should().Be(original.RawPrompt);
        hydrated.RawResponse.Should().Be(original.RawResponse);
        hydrated.StylePresetId.Should().Be(original.StylePresetId);

        // 4. Simulate deletion
        await repo.DeleteAsync(found.Id);
        var afterDelete = await repo.GetByIdAsync(found.Id);
        afterDelete.Should().BeNull();

        // 5. Restore full hydrated model (undo flow)
        await repo.SaveAsync(hydrated);
        var restored = await repo.GetByIdAsync(found.Id);
        restored.Should().NotBeNull();
        restored!.Id.Should().Be(original.Id);
        restored.Type.Should().Be(original.Type);
        restored.Title.Should().Be(original.Title);
        restored.ClassLevel.Should().Be(original.ClassLevel);
        restored.Subject.Should().Be(original.Subject);
        restored.IsFavorite.Should().Be(original.IsFavorite);
        restored.PlainText.Should().Be(original.PlainText);
        restored.Html.Should().Be(original.Html);
        restored.SourceJson.Should().Be(original.SourceJson);
        restored.StylePresetId.Should().Be(original.StylePresetId);
        restored.RawPrompt.Should().Be(original.RawPrompt);
        restored.RawResponse.Should().Be(original.RawResponse);
    }
}
