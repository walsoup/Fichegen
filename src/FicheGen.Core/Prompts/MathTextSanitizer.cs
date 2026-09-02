using System.Text.RegularExpressions;

namespace FicheGen.Core.Prompts;

/// <summary>
/// Converts the LaTeX fragments models keep leaking into "plain text"
/// exercises ($...$, \vec{AB}, \begin{pmatrix}…) into readable Unicode.
/// Defense in depth: exercise prompts already forbid LaTeX, but the renderer
/// has no math support, so raw markup must never reach the preview.
/// </summary>
public static partial class MathTextSanitizer
{
    private static readonly Dictionary<string, string> SymbolMap = new(StringComparer.Ordinal)
    {
        ["\\cdot"] = "·",
        ["\\times"] = "×",
        ["\\div"] = "÷",
        ["\\leq"] = "≤",
        ["\\le"] = "≤",
        ["\\geq"] = "≥",
        ["\\ge"] = "≥",
        ["\\neq"] = "≠",
        ["\\ne"] = "≠",
        ["\\approx"] = "≈",
        ["\\equiv"] = "≡",
        ["\\in"] = "∈",
        ["\\notin"] = "∉",
        ["\\subset"] = "⊂",
        ["\\subseteq"] = "⊆",
        ["\\cup"] = "∪",
        ["\\cap"] = "∩",
        ["\\emptyset"] = "∅",
        ["\\infty"] = "∞",
        ["\\to"] = "→",
        ["\\rightarrow"] = "→",
        ["\\Rightarrow"] = "⇒",
        ["\\iff"] = "⇔",
        ["\\Leftrightarrow"] = "⇔",
        ["\\forall"] = "∀",
        ["\\exists"] = "∃",
        ["\\partial"] = "∂",
        ["\\nabla"] = "∇",
        ["\\pm"] = "±",
        ["\\mp"] = "∓",
        ["\\circ"] = "∘",
        ["\\degree"] = "°",
        ["\\alpha"] = "α",
        ["\\beta"] = "β",
        ["\\gamma"] = "γ",
        ["\\delta"] = "δ",
        ["\\Delta"] = "Δ",
        ["\\theta"] = "θ",
        ["\\lambda"] = "λ",
        ["\\mu"] = "μ",
        ["\\pi"] = "π",
        ["\\sigma"] = "σ",
        ["\\phi"] = "φ",
        ["\\omega"] = "ω",
        ["\\Omega"] = "Ω",
        ["\\int"] = "∫",
        ["\\iint"] = "∬",
        ["\\iiint"] = "∭",
        ["\\sum"] = "∑",
        ["\\prod"] = "∏",
        ["\\lim"] = "lim",
        ["\\displaystyle"] = "",
        ["\\left"] = "",
        ["\\right"] = "",
        ["\\big"] = "",
        ["\\Big"] = "",
        ["\\bigg"] = "",
        ["\\quad"] = " ",
        ["\\qquad"] = "  ",
        ["\\;"] = " ",
        ["\\,"] = " ",
        ["\\!"] = "",
        ["\\ "] = " "
    };

    // Flattened to a lazily-computed array because replacement order is
    // load-bearing (see Clean) and Dictionary order is not guaranteed.
    private static readonly KeyValuePair<string, string>[] SymbolsLongestFirst =
        SymbolMap.OrderByDescending(kv => kv.Key.Length).ToArray();

    private static readonly Dictionary<string, string> BlackboardMap = new(StringComparer.Ordinal)
    {
        ["R"] = "ℝ", ["N"] = "ℕ", ["Z"] = "ℤ", ["Q"] = "ℚ", ["C"] = "ℂ"
    };

    private static readonly string[] MatrixEnvironments = { "pmatrix", "bmatrix", "matrix", "vmatrix", "cases" };

    [GeneratedRegex("\\$\\$(.+?)\\$\\$|\\$(.+?)\\$", RegexOptions.Singleline)]
    private static partial Regex DollarMathRegex();

    [GeneratedRegex("\\\\(?:mathbb|mathbf|mathcal|mathfrak|mathrm|mathit|text|operatorname|widehat|overline|bar|hat|vec)\\{([^{}]*)\\}")]
    private static partial Regex DecoratedCommandRegex();

    [GeneratedRegex(@"\\begin\{(pmatrix|bmatrix|matrix|vmatrix)\}(.*?)\\end\{\1\}", RegexOptions.Singleline)]
    private static partial Regex MatrixEnvironmentRegex();

    [GeneratedRegex(@"\\begin\{cases\}(.*?)\\end\{cases\}", RegexOptions.Singleline)]
    private static partial Regex CasesEnvironmentRegex();

    [GeneratedRegex(@"\\(?:begin|end)\{[a-zA-Z*]+\}")]
    private static partial Regex EnvironmentTagRegex();

    [GeneratedRegex(@"\\mathbb\{([^{}]*)\}")]
    private static partial Regex BlackboardRegex();

    [GeneratedRegex(@"\\sqrt\{([^{}]*)\}")]
    private static partial Regex SqrtRegex();

    [GeneratedRegex(@"\\(?:d|t)?frac\{([^{}]*)\}\{([^{}]*)\}")]
    private static partial Regex FractionRegex();

    [GeneratedRegex(@"\\([a-zA-Z]+)\{([^{}]*)\}")]
    private static partial Regex GenericCommandRegex();

    [GeneratedRegex(@"\\[a-zA-Z]+")]
    private static partial Regex OrphanCommandRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRegex();

    public static string Clean(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        var text = input;

        // Unwrap $…$ / $$…$$ math delimiters first.
        text = DollarMathRegex().Replace(text, m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

        // \mathbb{R} → ℝ before the generic command pass.
        text = BlackboardRegex().Replace(text, m =>
            BlackboardMap.TryGetValue(m.Groups[1].Value, out var symbol) ? symbol : m.Groups[1].Value);

        // \vec{AB} → AB, \widehat{BAC} → BAC (keep content, drop decoration).
        text = DecoratedCommandRegex().Replace(text, "$1");

        // Matrix-like environments become inline rows: (a b ; c d).
        text = MatrixEnvironmentRegex().Replace(text, m =>
        {
            var inner = m.Groups[2].Value.Replace("\\\\", " ; ").Replace("&", " ");
            inner = WhitespaceRegex().Replace(inner, " ").Trim();
            return $"({inner})";
        });

        // Cases environments become: cond1 ; cond2
        text = CasesEnvironmentRegex().Replace(text, m =>
        {
            var inner = m.Groups[1].Value.Replace("\\\\", " ; ").Replace("&", " ");
            return WhitespaceRegex().Replace(inner, " ").Trim();
        });

        // Strip non-matrix environment tags cleanly (\begin{center}...\end{center} -> ...)
        text = EnvironmentTagRegex().Replace(text, string.Empty);

        // Longest-first: "\le" is a prefix of "\left", "\in" of "\int",
        // "\ge" of "\geq", so shorter commands must never run first.
        foreach (var (command, symbol) in SymbolsLongestFirst)
        {
            text = text.Replace(command, symbol);
        }

        // Sqrt runs first so that nested fractions like \frac{\sqrt{2}}{2} simplify their components
        text = SqrtRegex().Replace(text, "√($1)");

        // \frac{a}{b}, \dfrac{a}{b}, \tfrac{a}{b} → (a)/(b)
        var prev = string.Empty;
        var loop = 0;
        while (prev != text && loop++ < 5)
        {
            prev = text;
            text = FractionRegex().Replace(text, "($1)/($2)");
        }

        // Generic single-argument commands like \foo{bar} -> bar
        text = GenericCommandRegex().Replace(text, m => m.Groups[1].Value switch
        {
            "vec" => m.Groups[2].Value,
            "frac" or "dfrac" or "tfrac" => $"({m.Groups[2].Value})",
            _ => m.Groups[2].Value
        });

        // Any remaining \command → dropped (e.g. \noindent), and any braces
        // left over from the passes above are plain-text noise.
        text = OrphanCommandRegex().Replace(text, string.Empty);
        text = text.Replace("{", string.Empty).Replace("}", string.Empty);

        // Collapse the whitespace the stripping left behind.
        text = WhitespaceRegex().Replace(text, " ").Trim();

        return text;
    }
}
