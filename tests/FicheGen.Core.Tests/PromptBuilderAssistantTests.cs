using FicheGen.Core.Prompts;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests;

public class PromptBuilderAssistantTests
{
    [Fact]
    public void BuildIntentPrompt_CreatesJsonResponseRequest()
    {
        var req = PromptBuilder.BuildIntentPrompt("Modifie le titre de la fiche");

        req.Purpose.Should().Be("intent");
        req.ResponseJson.Should().BeTrue();
        req.UserPrompt.Should().Be("Modifie le titre de la fiche");
    }

    [Fact]
    public void BuildEditPrompt_InjectsDocumentTextAndInstructions()
    {
        var req = PromptBuilder.BuildEditPrompt("Document actuel", "Ajoute un exercice 3");

        req.Purpose.Should().Be("chat");
        req.ResponseJson.Should().BeTrue();
        req.UserPrompt.Should().Contain("Document actuel");
        req.UserPrompt.Should().Contain("Ajoute un exercice 3");
    }

    [Fact]
    public void BuildQuestionPrompt_InjectsQuestionAndDocumentText()
    {
        var req = PromptBuilder.BuildQuestionPrompt("Texte de la fiche", "Quelle est la durée conseillée ?");

        req.Purpose.Should().Be("chat");
        req.ResponseJson.Should().BeFalse();
        req.UserPrompt.Should().Contain("Texte de la fiche");
        req.UserPrompt.Should().Contain("Quelle est la durée conseillée ?");
    }
}
