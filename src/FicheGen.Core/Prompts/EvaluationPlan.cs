using System.Text.Json;
using FicheGen.Core.Documents;

namespace FicheGen.Core.Prompts;

/// <summary>One planned exercise of an evaluation, with its barème allocation.</summary>
public sealed record EvalPlanExercise(
    string Title,
    int Points,
    string Competences,
    string? Format = null,
    bool HorsBareme = false);

/// <summary>
/// Structural plan of an evaluation produced by a first, fast LLM call. The
/// orchestrator validates and repairs it before any exercise content is
/// written, so the exercise count and the barème sum are enforced by code,
/// not trusted to the model.
/// </summary>
public sealed record EvalPlan(
    string? Title,
    string? Subtitle,
    IReadOnlyList<EvalPlanExercise> Exercises);

/// <summary>Content of a single exercise returned by one focused LLM call.</summary>
public sealed record EvalExerciseDraft(
    string Consigne,
    IReadOnlyList<string> Questions,
    IReadOnlyList<string> Corrige);

public static class EvalPlanParser
{
    private sealed record ExerciseDto(
        string? Title, double Points, string? Competences, string? Format, bool HorsBareme);

    private sealed record PlanDto(string? Title, string? Subtitle, List<ExerciseDto>? Exercises);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static bool TryParse(string raw, out EvalPlan? plan)
    {
        plan = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var cleaned = JsonCleaner.Clean(raw);
        if (string.IsNullOrWhiteSpace(cleaned))
            return false;

        PlanDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PlanDto>(cleaned, Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (dto?.Exercises is null || dto.Exercises.Count == 0)
            return false;

        var exercises = new List<EvalPlanExercise>();
        foreach (var e in dto.Exercises)
        {
            if (string.IsNullOrWhiteSpace(e.Title))
                return false;

            // Ungraded extension exercises carry no meaningful barème; their
            // points are ignored downstream, so any value is acceptable.
            if (!e.HorsBareme && e.Points < 1)
                return false;

            exercises.Add(new EvalPlanExercise(
                e.Title.Trim(),
                e.HorsBareme ? 0 : Math.Max(1, (int)Math.Round(e.Points)),
                e.Competences?.Trim() ?? string.Empty,
                e.Format?.Trim(),
                e.HorsBareme));
        }

        plan = new EvalPlan(dto.Title?.Trim(), dto.Subtitle?.Trim(), exercises);
        return true;
    }
}

/// <summary>
/// Deterministic barème/count repairs applied to a parsed plan. Point sums
/// are never trusted from the model (blueprint §5.2): the graded points are
/// adjusted to match the target exactly, and the exercise count is clamped
/// to the requested volume window.
/// </summary>
public static class EvalPlanNormalizer
{
    public static EvalPlan Normalize(EvalPlan plan, int targetPoints, int minCount, int maxCount)
    {
        var exercises = plan.Exercises.ToList();

        if (exercises.Count > maxCount)
            exercises = exercises.Take(maxCount).ToList();

        if (exercises.Count == 0)
            return plan;

        var gradedIndexes = Enumerable.Range(0, exercises.Count)
            .Where(i => !exercises[i].HorsBareme)
            .ToList();

        if (gradedIndexes.Count == 0)
            return plan;

        var sum = gradedIndexes.Sum(i => exercises[i].Points);
        var delta = targetPoints - sum;

        if (delta != 0)
        {
            var adjusted = exercises.ToArray();

            var guard = 0;
            while (delta != 0 && guard++ < 1000)
            {
                if (delta > 0)
                {
                    // Distribute extra points round-robin starting with lowest-point exercises
                    var sorted = gradedIndexes
                        .OrderBy(i => adjusted[i].Points)
                        .ToList();

                    foreach (var i in sorted)
                    {
                        if (delta == 0) break;
                        adjusted[i] = adjusted[i] with { Points = adjusted[i].Points + 1 };
                        delta--;
                    }
                }
                else
                {
                    // Reduce points from the largest graded exercises, never dropping below 1
                    var eligible = gradedIndexes
                        .Where(i => adjusted[i].Points > 1)
                        .OrderByDescending(i => adjusted[i].Points)
                        .ToList();

                    if (eligible.Count == 0)
                        break; // All graded exercises are at 1 point minimum

                    foreach (var i in eligible)
                    {
                        if (delta == 0) break;
                        if (adjusted[i].Points > 1)
                        {
                            adjusted[i] = adjusted[i] with { Points = adjusted[i].Points - 1 };
                            delta++;
                        }
                    }
                }
            }

            exercises = adjusted.ToList();
        }

        return plan with { Exercises = exercises };
    }
}

public static class EvalExerciseParser
{
    private sealed record DraftDto(string? Consigne, JsonElement? Questions, JsonElement? Corrige);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static bool TryParse(string raw, out EvalExerciseDraft? draft)
    {
        draft = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var cleaned = JsonCleaner.Clean(raw);
        if (string.IsNullOrWhiteSpace(cleaned))
            return false;

        DraftDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DraftDto>(cleaned, Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Consigne))
            return false;

        var questions = ToStringList(dto.Questions);
        if (questions.Count == 0)
            return false;

        draft = new EvalExerciseDraft(
            dto.Consigne.Trim(),
            questions,
            ToStringList(dto.Corrige));
        return true;
    }

    private static List<string> ToStringList(JsonElement? element)
    {
        var result = new List<string>();
        if (element is not JsonElement { ValueKind: JsonValueKind.Array } array)
            return result;

        foreach (var item in array.EnumerateArray())
        {
            var text = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object when item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String => t.GetString(),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(text))
                result.Add(text.Trim());
        }

        return result;
    }
}
