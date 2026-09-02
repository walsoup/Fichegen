using System.Globalization;
using System.Text.RegularExpressions;
using FicheGen.Core.Documents;

namespace FicheGen.Core.Prompts;

/// <summary>
/// Central evaluation-composition rules shared by the prompt builder and the
/// orchestrator's post-generation completeness check, so the prompt that asks
/// for a given volume and the code that verifies it never drift apart.
/// </summary>
public static partial class EvaluationSpec
{
    [GeneratedRegex(@"^\s*(?:exercice|partie|question|exercise|part|تمرين|مسألة|جزء)\s*[:.\-]?\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExerciseHeadingRegex();

    private static readonly string[] ExerciseTokens = ["exercice", "partie", "exercise", "part", "تمرين", "مسألة", "جزء"];

    private static bool ContainsExerciseToken(string text)
    {
        foreach (var token in ExerciseTokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the inclusive exercise-count target for the requested volume,
    /// scaled by the barème total (more points → more exercises).
    /// </summary>
    public static (int Min, int Max) GetExerciseCount(string? documentLength, int targetPoints)
    {
        var (min, max) = documentLength switch
        {
            "Bref" => (2, 3),
            "Raccourci" => (3, 4),
            "Long" => (4, 6),
            "Detaille" => (5, 7),
            "Exhaustif" => (6, 8),
            _ => (3, 5)
        };

        if (targetPoints > 30)
        {
            min++;
            max++;
        }
        else if (targetPoints <= 10)
        {
            min = Math.Max(2, min - 1);
            max = Math.Max(min + 1, max - 1);
        }

        return (min, max);
    }

    public static int GetMinimumExerciseCount(string? documentLength, int targetPoints)
        => GetExerciseCount(documentLength, targetPoints).Min;

    /// <summary>
    /// Pedagogical directive describing the requested difficulty on the 0..1
    /// internal scale (form difficulty divided by 5 in the UI).
    /// </summary>
    public static string GetDifficultyDirective(double difficulty)
    {
        return difficulty switch
        {
            <= 0.3 => "accessible — énoncés courts et questions simples, réussite attendue pour la grande majorité de la classe",
            <= 0.55 => "standard — difficulté progressive au sein de chaque exercice, avec quelques questions de consolidation",
            <= 0.75 => "intermédiaire — questions d'application et d'analyse, incluant au moins une question de transfert par exercice",
            <= 0.9 => "avancé — questions complexes en plusieurs étapes de raisonnement, justifications écrites attendues",
            _ => "exigeant — questions de maîtrise approfondie, situations de synthèse et raisonnements rédigés en plusieurs étapes"
        };
    }

    /// <summary>
    /// Counts distinct numbered exercise headings at the top level of the
    /// document (corrigé inner blocks are excluded because only top-level
    /// blocks are scanned). Unnumbered "Exercice …" headings are counted
    /// individually; duplicated numbers ("Exercice 1" twice) count once.
    /// </summary>
    public static int CountExercises(GeneratedDocument? document)
    {
        if (document is null)
            return 0;

        var numbers = new HashSet<int>();
        var unnumbered = 0;

        foreach (var block in document.Blocks)
        {
            if (block is not HeadingBlock heading)
                continue;

            var text = string.Concat(heading.Runs.Select(r => r.Text));
            var match = ExerciseHeadingRegex().Match(text);
            if (match.Success)
            {
                numbers.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            else if (ContainsExerciseToken(text))
            {
                unnumbered++;
            }
        }

        return numbers.Count + unnumbered;
    }
}
