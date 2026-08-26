using FicheGen.Core.Prompts;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Prompts;

public class PromptBuilderComprehensiveTests
{
    [Fact]
    public void BuildFichePrompt_WithInstructionsAndGuideContext_ConstructsValidRequest()
    {
        var parameters = new FicheParameters(
            ClassLevel: "CM2",
            Subject: "Histoire",
            Topic: "La Première Guerre Mondiale",
            DurationMinutes: 60,
            Instructions: "Inclure une étude de document et prévoir des adaptations DYS.",
            UsePedagogicalGuide: true
        );

        var guideContext = "Guide Nathan CM2 p. 142 : Séance 1 - Les tranchées et la vie quotidienne des poilus.";

        var request = PromptBuilder.BuildFichePrompt(parameters, guideContext);

        request.Should().NotBeNull();
        request.Purpose.Should().Be("fiche");
        request.ResponseJson.Should().BeTrue();
        request.Temperature.Should().Be(0.7);
        request.SystemPrompt.Should().NotBeNullOrWhiteSpace();

        request.UserPrompt.Should().Contain("CM2");
        request.UserPrompt.Should().Contain("Histoire");
        request.UserPrompt.Should().Contain("La Première Guerre Mondiale");
        request.UserPrompt.Should().Contain("60 minutes");
        request.UserPrompt.Should().Contain("adaptations DYS");
        request.UserPrompt.Should().Contain("EXTRAIT DU GUIDE PÉDAGOGIQUE OFFICIEL");
        request.UserPrompt.Should().Contain("Séance 1 - Les tranchées");
    }

    [Theory]
    [InlineData("sommative", 20)]
    [InlineData("formative", 10)]
    [InlineData("diagnostique", 15)]
    public void BuildEvalPrompt_VariedTypesAndPoints_ConstructsCorrectParameters(string evalType, int points)
    {
        var parameters = new EvalParameters(
            ClassLevel: "6e",
            Subject: "Mathématiques",
            Topic: "Les fractions et pourcentages",
            EvalType: evalType,
            TargetPoints: points,
            Instructions: "Exercices avec barème détaillé par question."
        );

        var request = PromptBuilder.BuildEvalPrompt(parameters, null);

        request.Should().NotBeNull();
        request.Purpose.Should().Be("eval");
        request.ResponseJson.Should().BeTrue();
        request.UserPrompt.Should().Contain(evalType);
        request.UserPrompt.Should().Contain($"{points} points au total");
        request.UserPrompt.Should().Contain("Mathématiques");
        request.UserPrompt.Should().Contain("Les fractions et pourcentages");
    }

    [Fact]
    public void BuildQuizPrompt_WithOptionsAndCounts_GeneratesAccurateInstructions()
    {
        var parameters = new QuizParameters(
            ClassLevel: "CE2",
            Subject: "Français",
            Topic: "Le présent de l'indicatif (verbes du 1er groupe)",
            QuestionCount: 10,
            DurationMinutes: 15,
            IncludeQcm: true,
            IncludeTrueFalse: true,
            IncludeShortAnswer: true,
            Instructions: "Corriger avec explications pour chaque question."
        );

        var request = PromptBuilder.BuildQuizPrompt(parameters);

        request.Should().NotBeNull();
        request.Purpose.Should().Be("quiz");
        request.ResponseJson.Should().BeTrue();
        request.UserPrompt.Should().Contain("10 questions");
        request.UserPrompt.Should().Contain("15 minutes");
        request.UserPrompt.Should().Contain("QCM");
        request.UserPrompt.Should().Contain("Vrai/Faux");
        request.UserPrompt.Should().Contain("Réponse courte");
        request.UserPrompt.Should().Contain("Le présent de l'indicatif");
    }
}
