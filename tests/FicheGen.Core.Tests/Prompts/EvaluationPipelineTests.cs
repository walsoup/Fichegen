using FicheGen.Core.Documents;
using FicheGen.Core.Prompts;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Prompts;

public class EvalPlanParserTests
{
    [Fact]
    public void TryParse_ValidPlanJson_ReturnsPlanWithExercises()
    {
        var raw = @"{
          ""title"": ""Évaluation : Les fractions"",
          ""subtitle"": ""Bilan de séquence"",
          ""exercises"": [
            { ""title"": ""Exercice 1 : Lecture de fractions"", ""points"": 5, ""competences"": ""Lire une fraction"", ""format"": ""réponses courtes"" },
            { ""title"": ""Exercice 2 : Comparaison"", ""points"": 10, ""competences"": ""Comparer des fractions"" },
            { ""title"": ""Pour aller plus loin"", ""points"": 3, ""competences"": ""Approfondissement"", ""horsBareme"": true }
          ]
        }";

        var ok = EvalPlanParser.TryParse(raw, out var plan);

        ok.Should().BeTrue();
        plan.Should().NotBeNull();
        plan!.Title.Should().Be("Évaluation : Les fractions");
        plan.Exercises.Should().HaveCount(3);
        plan.Exercises[0].Points.Should().Be(5);
        plan.Exercises[0].HorsBareme.Should().BeFalse();
        plan.Exercises[2].HorsBareme.Should().BeTrue();
    }

    [Fact]
    public void TryParse_WrappedInCodeFence_StillParses()
    {
        var raw = "```json\n{\"title\":\"T\",\"exercises\":[{\"title\":\"E1\",\"points\":20,\"competences\":\"c\"}]}\n```";

        EvalPlanParser.TryParse(raw, out var plan).Should().BeTrue();
        plan!.Exercises.Should().ContainSingle();
    }

    [Fact]
    public void TryParse_FullDocumentJsonInsteadOfPlan_ReturnsFalse()
    {
        // A single-shot document response (metadata + blocks) is not a plan.
        var raw = @"{ ""metadata"": { ""title"": ""Évaluation"" }, ""blocks"": [{ ""$type"": ""heading"", ""level"": 1, ""runs"": [{ ""text"": ""Exercice 1"" }] }] }";

        EvalPlanParser.TryParse(raw, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_MissingExercises_ReturnsFalse()
    {
        EvalPlanParser.TryParse(@"{ ""title"": ""Évaluation"" }", out _).Should().BeFalse();
        EvalPlanParser.TryParse("not json at all", out _).Should().BeFalse();
        EvalPlanParser.TryParse("", out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_ExerciseWithoutTitle_ReturnsFalse()
    {
        var raw = @"{ ""title"": ""T"", ""exercises"": [{ ""points"": 5, ""competences"": ""c"" }] }";

        EvalPlanParser.TryParse(raw, out _).Should().BeFalse();
    }
}

public class EvalPlanNormalizerTests
{
    private static EvalPlan MakePlan(params (string Title, int Points, bool HorsBareme)[] exercises) =>
        new("T", null, exercises.Select(e => new EvalPlanExercise(e.Title, e.Points, "c", null, e.HorsBareme)).ToList());

    [Fact]
    public void Normalize_SumTooLow_AddsPointsToReachTarget()
    {
        var plan = MakePlan(("E1", 5, false), ("E2", 5, false));

        var normalized = EvalPlanNormalizer.Normalize(plan, targetPoints: 20, minCount: 2, maxCount: 5);

        normalized.Exercises.Where(e => !e.HorsBareme).Sum(e => e.Points).Should().Be(20);
        normalized.Exercises.Should().HaveCount(2);
    }

    [Fact]
    public void Normalize_SumTooHigh_RemovesPointsToReachTarget()
    {
        var plan = MakePlan(("E1", 15, false), ("E2", 10, false));

        var normalized = EvalPlanNormalizer.Normalize(plan, targetPoints: 20, minCount: 2, maxCount: 5);

        normalized.Exercises.Where(e => !e.HorsBareme).Sum(e => e.Points).Should().Be(20);
        normalized.Exercises.All(e => e.Points >= 1).Should().BeTrue();
    }

    [Fact]
    public void Normalize_ExceedsMaxCount_TruncatesAndRebalances()
    {
        var plan = MakePlan(
            ("E1", 5, false), ("E2", 5, false), ("E3", 5, false),
            ("E4", 5, false), ("E5", 5, false), ("E6", 5, false),
            ("E7", 5, false), ("E8", 5, false), ("E9", 5, false));

        var normalized = EvalPlanNormalizer.Normalize(plan, targetPoints: 20, minCount: 6, maxCount: 8);

        normalized.Exercises.Should().HaveCount(8);
        normalized.Exercises.Where(e => !e.HorsBareme).Sum(e => e.Points).Should().Be(20);
    }

    [Fact]
    public void Normalize_HorsBaremeExcludedFromSum()
    {
        var plan = MakePlan(("E1", 20, false), ("Soutien", 0, true));

        var normalized = EvalPlanNormalizer.Normalize(plan, targetPoints: 20, minCount: 2, maxCount: 8);

        // The ungraded exercise never participates in the points arithmetic.
        normalized.Exercises.Where(e => !e.HorsBareme).Sum(e => e.Points).Should().Be(20);
    }

    [Fact]
    public void Normalize_AlreadyCorrect_ReturnsSamePoints()
    {
        var plan = MakePlan(("E1", 12, false), ("E2", 8, false));

        var normalized = EvalPlanNormalizer.Normalize(plan, targetPoints: 20, minCount: 2, maxCount: 5);

        normalized.Exercises[0].Points.Should().Be(12);
        normalized.Exercises[1].Points.Should().Be(8);
    }
}

public class MathTextSanitizerTests
{
    [Theory]
    [InlineData("Calcule $2x + 3$ puis conclut.", "Calcule 2x + 3 puis conclut.")]
    [InlineData("$$\\int_0^1 x\\,dx$$", "∫_0^1 x dx")]
    [InlineData("Soient $\\vec{AB}$ et $\\vec{AC}$", "Soient AB et AC")]
    [InlineData("Dans $\\mathbb{R}^2$", "Dans ℝ^2")]
    [InlineData("$A^2 - 5A + 6I = O_2$", "A^2 - 5A + 6I = O_2")]
    [InlineData("Le plan $5x + y - 2z - 1 = 0$", "Le plan 5x + y - 2z - 1 = 0")]
    [InlineData("Montrer que $\\frac{1}{2} + \\frac{1}{3} = \\frac{5}{6}$", "Montrer que (1)/(2) + (1)/(3) = (5)/(6)")]
    [InlineData("$\\sqrt{2} \\times \\sqrt{3} \\ge \\sqrt{5}$", "√(2) × √(3) ≥ √(5)")]
    [InlineData("L'angle $\\widehat{BAC}$ est obtuse car $\\vec{AB} \\cdot \\vec{AC} < 0$", "L'angle BAC est obtuse car AB · AC < 0")]
    [InlineData("$2 \\leq 3$ et $4 \\neq 5$", "2 ≤ 3 et 4 ≠ 5")]
    [InlineData("Pour tout $n \\in \\mathbb{N}$, $n \\to \\infty$", "Pour tout n ∈ ℕ, n → ∞")]
    [InlineData("Texte sans aucun markup", "Texte sans aucun markup")]
    [InlineData("Calculer $\\dfrac{1}{2} + \\tfrac{1}{3}$", "Calculer (1)/(2) + (1)/(3)")]
    [InlineData("Résultat $\\frac{\\sqrt{2}}{2}$", "Résultat (√(2))/(2)")]
    [InlineData("M. & Mme Dupont travaillent en R&D", "M. & Mme Dupont travaillent en R&D")]
    [InlineData("\\begin{center} Remarque importante \\end{center}", "Remarque importante")]
    public void Clean_ConvertsLatexFragments_ToPlainText(string input, string expected)
    {
        MathTextSanitizer.Clean(input).Should().Be(expected);
    }

    [Fact]
    public void Clean_MatrixEnvironment_FlattensToInlineRow()
    {
        var input = "$A = \\begin{pmatrix} 2 & 1 \\\\ 0 & 3 \\end{pmatrix}$";

        var result = MathTextSanitizer.Clean(input);

        result.Should().NotContain("\\begin");
        result.Should().NotContain("pmatrix");
        result.Should().Contain("2 1 ; 0 3");
        result.Should().NotContain("$");
    }

    [Fact]
    public void Clean_CasesEnvironment_FlattensWithoutParens()
    {
        var input = "$\\begin{cases} 2x + y = 7 \\\\ 3y = 9 \\end{cases}$";

        var result = MathTextSanitizer.Clean(input);

        result.Should().NotContain("\\begin");
        result.Should().NotContainAny("$", "{", "}");
        result.Should().Contain("2x + y = 7 ; 3y = 9");
    }

    [Fact]
    public void Clean_NullOrEmpty_ReturnsEmpty()
    {
        MathTextSanitizer.Clean(null).Should().BeEmpty();
        MathTextSanitizer.Clean(string.Empty).Should().BeEmpty();
    }
}

public class EvalExerciseParserTests
{
    [Fact]
    public void TryParse_ValidDraft_ReturnsQuestionsAndCorrige()
    {
        var raw = @"{
          ""consigne"": ""Calcule les produits scalaires suivants."",
          ""questions"": [""a) AB · AC"", ""b) Déduis l'angle.""],
          ""corrige"": [""a) -6 (1 pt)"", ""b) obtus (1 pt)""]
        }";

        var ok = EvalExerciseParser.TryParse(raw, out var draft);

        ok.Should().BeTrue();
        draft!.Consigne.Should().Contain("produits scalaires");
        draft.Questions.Should().HaveCount(2);
        draft.Corrige.Should().HaveCount(2);
    }

    [Fact]
    public void TryParse_QuestionsAsObjectsWithTextField_AreAccepted()
    {
        var raw = @"{ ""consigne"": ""C"", ""questions"": [{ ""text"": ""a) q1"" }, { ""text"": ""b) q2"" }] }";

        var ok = EvalExerciseParser.TryParse(raw, out var draft);

        ok.Should().BeTrue();
        draft!.Questions.Should().BeEquivalentTo(new[] { "a) q1", "b) q2" });
    }

    [Fact]
    public void TryParse_MissingConsigneOrQuestions_ReturnsFalse()
    {
        EvalExerciseParser.TryParse(@"{ ""questions"": [""a)""] }", out _).Should().BeFalse();
        EvalExerciseParser.TryParse(@"{ ""consigne"": ""C"" }", out _).Should().BeFalse();
        EvalExerciseParser.TryParse("markdown instead", out _).Should().BeFalse();
    }
}

public class EvaluationAssemblerTests
{
    private static EvalParameters Parameters(string? language = "fr-FR") =>
        new("Terminale", "Mathématiques", "Vecteurs et matrices", EvalType: "sommative",
            TargetPoints: 20, Difficulty: 0.8, Language: language, DocumentLength: "Exhaustif", DurationMinutes: 60);

    private static EvalPlan MakePlan() => new(
        "Évaluation : Vecteurs et matrices",
        "Bilan de séquence",
        new List<EvalPlanExercise>
        {
            new("Exercice 1 : Géométrie dans l'espace", 6, "Calcul vectoriel", "calculs"),
            new("Exercice 2 : Matrices", 8, "Calcul matriciel", "vrai/faux justifié"),
            new("Exercice 3 : Intégrales", 6, "Intégration", "problème ouvert"),
            new("Pour aller plus loin", 3, "Approfondissement", "problème ouvert", HorsBareme: true)
        });

    private static List<EvalExerciseDraft> MakeDrafts(int count) => Enumerable.Range(0, count)
        .Select(i => new EvalExerciseDraft(
            $"Consigne de l'exercice {i + 1}.",
            new[] { $"a) question ${i}$", $"b) question $\\vec{{AB}}$" },
            new[] { $"a) réponse ${i}$", "b) réponse AB" }))
        .ToList();

    [Fact]
    public void Assemble_Structure_FollowsPlanDeterministically()
    {
        var doc = EvaluationAssembler.Assemble(Parameters(), MakePlan(), MakeDrafts(4));

        EvaluationSpec.CountExercises(doc).Should().Be(3); // graded only, numbered
        doc.Metadata.DocType.Should().Be("evaluation");
        doc.Metadata.Duration.Should().Be(60);
        doc.Metadata.Language.Should().Be("fr-FR");
        doc.SourceJson.Should().NotBeNullOrWhiteSpace();

        // Student identity grid, 3 exercises, barème table, extension, page break, corrigé.
        doc.Blocks[0].Should().BeOfType<KeyValueGridBlock>();
        doc.Blocks.Should().Contain(b => b is TableBlock);
        doc.Blocks.Should().Contain(b => b is PageBreakBlock);
        doc.Blocks.Last().Should().BeOfType<CalloutBoxBlock>().Which.Kind.Should().Be("corrige");

        var extensionHeading = doc.Blocks.OfType<HeadingBlock>()
            .Single(h => h.Level == 1 && string.Concat(h.Runs.Select(r => r.Text)).Contains("Pour aller plus loin"));
        extensionHeading.Should().NotBeNull();
    }

    [Fact]
    public void Assemble_BaremeTable_SumsExactlyToTarget()
    {
        var doc = EvaluationAssembler.Assemble(Parameters(), MakePlan(), MakeDrafts(4));

        var table = doc.Blocks.OfType<TableBlock>().Single();
        var pointsColumn = table.Headers.Count - 1;
        var gradedRows = table.Rows
            .Select(r => r[pointsColumn])
            .Select(v => double.Parse(v.TrimEnd(' ', 'p', 't', 's'), System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        gradedRows.Sum().Should().Be(20);
    }

    [Fact]
    public void Assemble_ModelDriftInPoints_IsIgnoredPlanPointsAreUsed()
    {
        // Draft content claims wrong point values; the assembled barème must
        // come from the (normalized) plan, never from the model's prose.
        var plan = new EvalPlan("T", null, new List<EvalPlanExercise>
        {
            new("Exercice 1", 10, "c", null),
            new("Exercice 2", 10, "c", null)
        });
        var drafts = new List<EvalExerciseDraft>
        {
            new("Consigne 1 (99 points).", new[] { "a) q" }, new[] { "a) r (99 pts)" }),
            new("Consigne 2 (99 points).", new[] { "a) q" }, new[] { "a) r (99 pts)" })
        };

        var doc = EvaluationAssembler.Assemble(Parameters(), plan, drafts);

        var table = doc.Blocks.OfType<TableBlock>().Single();
        table.Rows.Should().HaveCount(2);
        table.Rows[0][^1].Should().Be("10");
        table.Rows[1][^1].Should().Be("10");
    }

    [Fact]
    public void Assemble_SanitizesLatexFromAllModelText()
    {
        var doc = EvaluationAssembler.Assemble(Parameters(), MakePlan(), MakeDrafts(4));

        var json = doc.SourceJson!;
        json.Should().NotContain("\\vec");
        json.Replace("\"$type\"", "").Should().NotContain("$");
        var allText = string.Join(" ", doc.Blocks.OfType<ParagraphBlock>().Select(p => string.Concat(p.Runs.Select(r => r.Text))));
        allText.Should().NotContainAny("$", "\\vec");
    }

    [Fact]
    public void Assemble_EnglishLanguage_UsesEnglishLabels()
    {
        var doc = EvaluationAssembler.Assemble(Parameters("en-US"), MakePlan(), MakeDrafts(4));

        var html = HtmlRenderer.RenderToFragment(doc);
        html.Should().Contain("Evaluation");
        html.Should().NotContain("Fiche Pédagogique");
        html.Should().Contain("Level:");
        doc.Blocks.OfType<HeadingBlock>()
            .Any(h => string.Concat(h.Runs.Select(r => r.Text)).Contains("Exercice 1"))
            .Should().BeFalse("English documents should use 'Exercise N' headings");
    }

    [Fact]
    public void Assemble_EmptyCorrige_ShowsLocalizedMissingNote()
    {
        var plan = new EvalPlan("T", null, new List<EvalPlanExercise> { new("Exercice 1", 20, "c", null) });
        var drafts = new List<EvalExerciseDraft> { new("Consigne.", new[] { "a) q" }, Array.Empty<string>()) };

        var doc = EvaluationAssembler.Assemble(Parameters(), plan, drafts);

        var corrigé = doc.Blocks.OfType<CalloutBoxBlock>().Single();
        corrigé.ContentBlocks.OfType<ParagraphBlock>()
            .Should().Contain(p => string.Concat(p.Runs.Select(r => r.Text)).Contains("corrigé non disponible"));
    }
}
