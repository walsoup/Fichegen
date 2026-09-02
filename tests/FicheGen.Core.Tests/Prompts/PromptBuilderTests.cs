using FicheGen.Core.Documents;
using FicheGen.Core.Prompts;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Prompts;

public class PromptBuilderTests
{
    [Fact]
    public void BuildFichePrompt_IncludesClassLevelSubjectAndTopic()
    {
        var p = new FicheParameters("CM2", "Mathématiques", "Les fractions et décimaux", DurationMinutes: 45);

        var req = PromptBuilder.BuildFichePrompt(p);

        req.Purpose.Should().Be("fiche");
        req.ResponseJson.Should().BeTrue();
        req.UserPrompt.Should().Contain("CM2");
        req.UserPrompt.Should().Contain("Mathématiques");
        req.UserPrompt.Should().Contain("Les fractions et décimaux");
    }

    [Fact]
    public void BuildEvalPrompt_IncludesBaremeTarget()
    {
        var p = new EvalParameters("3e", "Histoire", "La Seconde Guerre Mondiale", TargetPoints: 20);

        var req = PromptBuilder.BuildEvalPrompt(p);

        req.Purpose.Should().Be("eval");
        req.UserPrompt.Should().Contain("3e");
        req.UserPrompt.Should().Contain("20 points");
    }

    [Fact]
    public void BuildEvalPrompt_Exhaustif_DemandsMinimumExerciseCount()
    {
        var p = new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20, DocumentLength: "Exhaustif");

        var req = PromptBuilder.BuildEvalPrompt(p);

        req.UserPrompt.Should().Contain("entre 6 et 8 exercices");
    }

    [Fact]
    public void BuildEvalPrompt_DefaultLength_DemandsDefaultExerciseCount()
    {
        var p = new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20);

        var req = PromptBuilder.BuildEvalPrompt(p);

        req.UserPrompt.Should().Contain("entre 3 et 5 exercices");
    }

    [Fact]
    public void BuildEvalPrompt_IncludesDuration()
    {
        var p = new EvalParameters("CM2", "Mathématiques", "Fractions", DurationMinutes: 90);

        var req = PromptBuilder.BuildEvalPrompt(p);

        req.UserPrompt.Should().Contain("90 minutes");
    }

    [Theory]
    [InlineData(1.0, "accessible")]
    [InlineData(2.5, "standard")]
    [InlineData(5.0, "exigeant")]
    public void BuildEvalPrompt_IncludesDifficultyDirective(double difficultyOnFive, string expectedKeyword)
    {
        var p = new EvalParameters("CM2", "Mathématiques", "Fractions", Difficulty: difficultyOnFive / 5.0);

        var req = PromptBuilder.BuildEvalPrompt(p);

        req.UserPrompt.Should().Contain(expectedKeyword);
    }

    [Fact]
    public void BuildEvalPrompt_LongFormat_AsksForDifferentiationSection()
    {
        var p = new EvalParameters("CM2", "Mathématiques", "Fractions", DocumentLength: "Exhaustif");

        var req = PromptBuilder.BuildEvalPrompt(p);

        req.UserPrompt.Should().Contain("Différenciation");
    }

    [Fact]
    public void BuildEvalExpansionPrompt_AskForMoreExercises_KeepExistingOnes()
    {
        var doc = new GeneratedDocument(
            new DocumentMetadata("Évaluation Fractions", DocType: "evaluation"),
            new List<Block>
            {
                new HeadingBlock(1, "Exercice 1 (20 points)")
            });

        var req = PromptBuilder.BuildEvalExpansionPrompt(
            doc, topic: "Fractions", classLevel: "CM2",
            currentExerciseCount: 1, minimumExerciseCount: 6,
            targetPoints: 20, documentLength: "Exhaustif");

        req.Purpose.Should().Be("eval");
        req.ResponseJson.Should().BeTrue();
        req.UserPrompt.Should().Contain("6 exercices");
        req.UserPrompt.Should().Contain("Conserve intégralement");
        req.UserPrompt.Should().Contain("Fractions");
        req.UserPrompt.Should().Contain("20 points");
    }

    [Fact]
    public void BuildQuizPrompt_IncludesQuestionTypesAndCount()
    {
        var p = new QuizParameters("CM1", "Sciences", "Le cycle de l'eau", QuestionCount: 10, DurationMinutes: 15, IncludeQcm: true, IncludeTrueFalse: true, IncludeShortAnswer: true);

        var req = PromptBuilder.BuildQuizPrompt(p);

        req.Purpose.Should().Be("quiz");
        req.UserPrompt.Should().Contain("CM1");
        req.UserPrompt.Should().Contain("10 questions");
        req.UserPrompt.Should().Contain("QCM");
        req.UserPrompt.Should().Contain("Vrai/Faux");
        req.UserPrompt.Should().Contain("Réponse courte");
    }
}
