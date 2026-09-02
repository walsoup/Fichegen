using FicheGen.Core.Documents;
using FicheGen.Core.Prompts;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Prompts;

public class EvaluationSpecTests
{
    [Theory]
    [InlineData("Bref", 20, 2, 3)]
    [InlineData("Raccourci", 20, 3, 4)]
    [InlineData("Defaut", 20, 3, 5)]
    [InlineData("Long", 20, 4, 6)]
    [InlineData("Detaille", 20, 5, 7)]
    [InlineData("Exhaustif", 20, 6, 8)]
    [InlineData(null, 20, 3, 5)]
    [InlineData("Exhaustif", 40, 7, 9)]
    [InlineData("Exhaustif", 10, 5, 7)]
    [InlineData("Bref", 10, 2, 3)]
    [InlineData("Bref", 40, 3, 4)]
    public void GetExerciseCount_VariesByLengthAndPoints(string? documentLength, int targetPoints, int expectedMin, int expectedMax)
    {
        var (min, max) = EvaluationSpec.GetExerciseCount(documentLength, targetPoints);

        min.Should().Be(expectedMin);
        max.Should().Be(expectedMax);
        min.Should().BeLessThanOrEqualTo(max);
    }

    [Fact]
    public void GetMinimumExerciseCount_Exhaustif_ReturnsAtLeastSix()
    {
        EvaluationSpec.GetMinimumExerciseCount("Exhaustif", 20).Should().BeGreaterThanOrEqualTo(6);
    }

    [Theory]
    [InlineData(0.2, "accessible")]
    [InlineData(0.5, "standard")]
    [InlineData(0.6, "intermédiaire")]
    [InlineData(0.8, "avancé")]
    [InlineData(1.0, "exigeant")]
    public void GetDifficultyDirective_VariesByDifficulty(double difficulty, string expectedKeyword)
    {
        EvaluationSpec.GetDifficultyDirective(difficulty).Should().Contain(expectedKeyword);
    }

    [Fact]
    public void CountExercises_NumberedHeadings_CountsDistinctNumbers()
    {
        var doc = new GeneratedDocument(
            new DocumentMetadata("Évaluation"),
            new List<Block>
            {
                new HeadingBlock(1, "Exercice 1 (5 points)"),
                new ParagraphBlock("Consigne Exercice 1…"),
                new HeadingBlock(1, "Exercice 2 (8 points)"),
                new HeadingBlock(1, "Exercice 1 (7 points)"), // duplicate number, not a new exercise
                new HeadingBlock(2, "Corrigé Enseignant")
            });

        EvaluationSpec.CountExercises(doc).Should().Be(2);
    }

    [Fact]
    public void CountExercises_UnnumberedExerciseHeadings_CountsEachHeading()
    {
        var doc = new GeneratedDocument(
            new DocumentMetadata("Évaluation"),
            new List<Block>
            {
                new HeadingBlock(1, "Exercice sur les fractions"),
                new HeadingBlock(1, "Exercice sur les décimaux"),
                new HeadingBlock(1, "Barème")
            });

        EvaluationSpec.CountExercises(doc).Should().Be(2);
    }

    [Fact]
    public void CountExercises_NullOrEmptyDocument_ReturnsZero()
    {
        EvaluationSpec.CountExercises(null).Should().Be(0);

        var empty = new GeneratedDocument(new DocumentMetadata("Évaluation"), new List<Block>());
        EvaluationSpec.CountExercises(empty).Should().Be(0);
    }

    [Fact]
    public void CountExercises_EnglishHeadings_CountsExercisesCorrectly()
    {
        var doc = new GeneratedDocument(
            new DocumentMetadata("Evaluation", Language: "en-US"),
            new List<Block>
            {
                new HeadingBlock(1, "Exercise 1 (5 points)"),
                new HeadingBlock(1, "Exercise 2 (10 points)"),
                new HeadingBlock(1, "Part 3 (5 points)")
            });

        EvaluationSpec.CountExercises(doc).Should().Be(3);
    }

    [Fact]
    public void CountExercises_ArabicHeadings_CountsExercisesCorrectly()
    {
        var doc = new GeneratedDocument(
            new DocumentMetadata("تقييم", Language: "ar-SA"),
            new List<Block>
            {
                new HeadingBlock(1, "تمرين 1 (5 نقط)"),
                new HeadingBlock(1, "تمرين 2 (10 نقط)"),
                new HeadingBlock(1, "مسألة 3 (5 نقط)")
            });

        EvaluationSpec.CountExercises(doc).Should().Be(3);
    }
}
