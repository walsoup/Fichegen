using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using FicheGen.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class AssistantServiceTests
{
    [Fact]
    public void ClassifyIntentHeuristic_CorrectlyIdentifiesKeywords()
    {
        AssistantService.ClassifyIntentHeuristic("Modifie le titre de la fiche").Should().Be(IntentKind.Modifier);
        AssistantService.ClassifyIntentHeuristic("Ajoute un exercice 4").Should().Be(IntentKind.Modifier);
        AssistantService.ClassifyIntentHeuristic("Génère une évaluation CM2").Should().Be(IntentKind.Generer);
        AssistantService.ClassifyIntentHeuristic("Comment aborder cette notion ?").Should().Be(IntentKind.Question);
    }

    [Fact]
    public void UndoStack_PushesAndPopsWithMaxDepth10()
    {
        var service = new AssistantService(null!);

        service.CanUndo.Should().BeFalse();

        for (int i = 1; i <= 15; i++)
        {
            service.PushUndo(new GeneratedDocument(new DocumentMetadata($"Doc {i}"), new List<Block>()));
        }

        service.CanUndo.Should().BeTrue();

        // Popping should yield Doc 15 down to Doc 6 (since capacity is capped at 10)
        var poppedLast = service.PopUndo();
        poppedLast.Should().NotBeNull();
        poppedLast!.Metadata.Title.Should().Be("Doc 15");

        for (int i = 0; i < 8; i++)
        {
            service.PopUndo();
        }

        var poppedOldest = service.PopUndo();
        poppedOldest!.Metadata.Title.Should().Be("Doc 6");

        service.CanUndo.Should().BeFalse();
    }
}
