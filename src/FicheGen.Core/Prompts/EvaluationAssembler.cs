using System.Text.RegularExpressions;
using FicheGen.Core.Documents;

namespace FicheGen.Core.Prompts;

/// <summary>
/// Deterministically assembles the final evaluation document from a
/// validated plan and the per-exercise drafts. Exercise numbering, barème
/// table, total row and corrigé structure are produced by this code, so the
/// model can no longer omit exercises or break the points total.
/// </summary>
public static partial class EvaluationAssembler
{
    [GeneratedRegex(@"^\s*(?:exercice|exercise|partie|part|question|تمرين|مسألة|جزء)\s*\d*\s*[:.\-–—]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ExercisePrefixRegex();

    [GeneratedRegex(@"\s*[\(\[]\s*\d+\s*(?:points?|pts?|نقطة|نقاط)?\s*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex ExistingPointsRegex();

    [GeneratedRegex(@"^\s*(?:\d+[\.\)]|[a-zA-Z][\.\)]|\([0-9a-zA-Z]\))\s*")]
    private static partial Regex ListPrefixRegex();

    public static GeneratedDocument Assemble(
        EvalParameters parameters,
        EvalPlan plan,
        IReadOnlyList<EvalExerciseDraft> drafts,
        string? fallbackTitle = null)
    {
        if (drafts.Count != plan.Exercises.Count)
            throw new ArgumentException("One draft per planned exercise is required.", nameof(drafts));

        var labels = DocumentLabelsResolver.Resolve("evaluation", parameters.Language);
        var blocks = new List<Block>();

        blocks.Add(new KeyValueGridBlock(new List<KeyValuePair<string, string>>
        {
            new($"{labels.Name} / {labels.FirstName}", "..................................."),
            new(labels.Date, "...................."),
            new(labels.Grade, $"          / {parameters.TargetPoints}")
        }));

        var gradedRows = new List<List<string>>();
        var gradedExercises = new List<(EvalPlanExercise Plan, EvalExerciseDraft Draft, int Number)>();
        var extensionExercises = new List<(EvalPlanExercise Plan, EvalExerciseDraft Draft)>();
        var number = 0;

        for (var i = 0; i < plan.Exercises.Count; i++)
        {
            var exercise = plan.Exercises[i];
            var draft = drafts[i];

            if (exercise.HorsBareme)
            {
                extensionExercises.Add((exercise, draft));
                continue;
            }

            number++;
            gradedExercises.Add((exercise, draft, number));
            blocks.Add(BuildExerciseHeading(labels, number, exercise.Points, exercise.Title, graded: true, parameters.Language));
            AppendExerciseBody(blocks, draft);
            gradedRows.Add(new List<string>
            {
                $"{labels.Exercise} {number}",
                MathTextSanitizer.Clean(exercise.Competences),
                exercise.Points.ToString()
            });
        }

        if (gradedExercises.Count > 0)
        {
            blocks.Add(new TableBlock(
                new List<string> { labels.Exercise, labels.SkillsHeader, labels.PointsHeader },
                gradedRows,
                null));
        }

        if (extensionExercises.Count > 0)
        {
            blocks.Add(new HeadingBlock(1, labels.ExtensionHeading));
            foreach (var (plan2, draft) in extensionExercises)
            {
                blocks.Add(new HeadingBlock(2, $"{MathTextSanitizer.Clean(plan2.Title)} ({labels.Ungraded})"));
                AppendExerciseBody(blocks, draft);
            }
        }

        blocks.Add(new PageBreakBlock());
        var corrigéBlocks = new List<Block>
        {
            new HeadingBlock(2, labels.CorrectionHeading)
        };
        foreach (var (exercise, draft, exerciseNumber) in gradedExercises)
        {
            corrigéBlocks.Add(BuildExerciseHeading(labels, exerciseNumber, exercise.Points, exercise.Title, graded: true, parameters.Language));
            corrigéBlocks.Add(draft.Corrige.Count > 0
                ? new NumberedListBlock(draft.Corrige.Select(c =>
                {
                    var stripped = ListPrefixRegex().Replace(c, string.Empty).Trim();
                    var text = string.IsNullOrWhiteSpace(stripped) ? c : stripped;
                    return new List<TextRun> { new(MathTextSanitizer.Clean(text)) };
                }).ToList())
                : new ParagraphBlock(labels.MissingCorrige));
        }

        foreach (var (exercise, draft) in extensionExercises)
        {
            corrigéBlocks.Add(new HeadingBlock(2, $"{MathTextSanitizer.Clean(exercise.Title)} ({labels.Ungraded})"));
            corrigéBlocks.Add(draft.Corrige.Count > 0
                ? new NumberedListBlock(draft.Corrige.Select(c =>
                {
                    var stripped = ListPrefixRegex().Replace(c, string.Empty).Trim();
                    var text = string.IsNullOrWhiteSpace(stripped) ? c : stripped;
                    return new List<TextRun> { new(MathTextSanitizer.Clean(text)) };
                }).ToList())
                : new ParagraphBlock(labels.MissingCorrige));
        }

        blocks.Add(new CalloutBoxBlock("corrige", corrigéBlocks));

        var title = !string.IsNullOrWhiteSpace(plan.Title)
            ? MathTextSanitizer.Clean(plan.Title)
            : fallbackTitle ?? $"{labels.Kicker} : {parameters.Topic}";

        var doc = new GeneratedDocument(
            new DocumentMetadata(
                Title: title,
                Subtitle: string.IsNullOrWhiteSpace(plan.Subtitle) ? null : MathTextSanitizer.Clean(plan.Subtitle),
                ClassLevel: parameters.ClassLevel,
                Subject: parameters.Subject,
                Duration: parameters.DurationMinutes,
                Date: parameters.CurrentDate,
                DocType: "evaluation",
                Language: parameters.Language),
            blocks);

        return doc with { SourceJson = System.Text.Json.JsonSerializer.Serialize(doc) };
    }

    private static HeadingBlock BuildExerciseHeading(
        DocumentLabels labels, int number, int points, string title, bool graded, string? language = null)
    {
        var shortTitle = ExercisePrefixRegex().Replace(title, string.Empty);
        shortTitle = ExistingPointsRegex().Replace(shortTitle, string.Empty).Trim(' ', ':', '-', '–', '—');

        var pointsLabel = language != null && language.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
            ? (points == 1 ? "نقطة واحدة" : points == 2 ? "نقطتان" : points <= 10 ? $"{points} نقاط" : $"{points} نقطة")
            : (points == 1 ? "1 point" : $"{points} points");

        var headingText = string.IsNullOrWhiteSpace(shortTitle)
            ? $"{labels.Exercise} {number} ({pointsLabel})"
            : $"{labels.Exercise} {number} ({pointsLabel}) — {MathTextSanitizer.Clean(shortTitle)}";
        return new HeadingBlock(1, headingText);
    }

    private static void AppendExerciseBody(List<Block> blocks, EvalExerciseDraft draft)
    {
        if (!string.IsNullOrWhiteSpace(draft.Consigne))
        {
            blocks.Add(new ParagraphBlock(MathTextSanitizer.Clean(draft.Consigne)));
        }

        if (draft.Questions.Count > 0)
        {
            blocks.Add(new NumberedListBlock(
                draft.Questions.Select(q =>
                {
                    var stripped = ListPrefixRegex().Replace(q, string.Empty).Trim();
                    var text = string.IsNullOrWhiteSpace(stripped) ? q : stripped;
                    return new List<TextRun> { new(MathTextSanitizer.Clean(text)) };
                }).ToList()));
        }
    }
}
