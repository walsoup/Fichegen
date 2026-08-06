using System.Text.RegularExpressions;

namespace FicheGen.Core.Documents;

public static partial class CssSanitizer
{
    private static readonly Regex DangerousConstructsRegex = new(
        @"(?i)(@import|url\s*\(|javascript\s*:|expression\s*\(|<script|position\s*:\s*fixed|position\s*:\s*absolute|behavior\s*:)",
        RegexOptions.Compiled);

    public static string SanitizeCss(string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
        {
            return string.Empty;
        }

        // Split into rules or lines and filter out dangerous constructs
        var sanitizedLines = new List<string>();
        var lines = css.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (DangerousConstructsRegex.IsMatch(trimmed))
            {
                continue; // Skip dangerous line
            }

            sanitizedLines.Add(trimmed);
        }

        return string.Join("\n", sanitizedLines);
    }
}
