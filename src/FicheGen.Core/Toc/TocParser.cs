using System.Text;
using System.Text.RegularExpressions;

namespace FicheGen.Core.Toc;

public static class TocParser
{
    private static readonly Regex Tier1DotLeaders = new(@"^(.{3,120}?)\s*\.{2,}\s*(\d{1,4})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Tier2ColumnSeparated = new(@"^(.{3,120}?)\s{2,}(\d{1,4})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Tier3LeadingPage = new(@"^(\d{1,4})\s*[–\-\.]\s*(.{3,120})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FilterKeywords = new(@"\b(SOMMAIRE|TABLE|PAGE|CONTENTS|INDEX)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<(string Title, int PrintedPage)> ParseToc(string tocPagesText)
    {
        if (string.IsNullOrWhiteSpace(tocPagesText))
            return Array.Empty<(string, int)>();

        var normalized = NormalizeText(tocPagesText);
        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Try Tier 1
        var entries = ExtractTier(lines, Tier1DotLeaders, titleGroup: 1, pageGroup: 2);
        if (entries.Count >= 5) return entries;

        // Try Tier 2
        entries = ExtractTier(lines, Tier2ColumnSeparated, titleGroup: 1, pageGroup: 2);
        if (entries.Count >= 5) return entries;

        // Try Tier 3
        entries = ExtractTier(lines, Tier3LeadingPage, titleGroup: 2, pageGroup: 1);
        return entries;
    }

    private static IReadOnlyList<(string Title, int PrintedPage)> ExtractTier(string[] lines, Regex pattern, int titleGroup, int pageGroup)
    {
        var result = new List<(string Title, int PrintedPage)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var match = pattern.Match(line);
            if (!match.Success) continue;

            var title = match.Groups[titleGroup].Value.Trim();
            var pageStr = match.Groups[pageGroup].Value.Trim();

            if (title.Length < 3 || title.Length > 120) continue;
            if (FilterKeywords.IsMatch(title)) continue;
            if (!int.TryParse(pageStr, out var printedPage) || printedPage <= 0 || printedPage > 2000) continue;

            var key = $"{title}|{printedPage}";
            if (seen.Add(key))
            {
                result.Add((title, printedPage));
            }
        }

        return result;
    }

    private static string NormalizeText(string text)
    {
        var formC = text.Normalize(NormalizationForm.FormC);
        var lines = formC.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            // Normalize tabs and multi-spaces to 2 spaces so column gaps are preserved
            var replaced = line.Replace('\t', ' ');
            var collapsed = Regex.Replace(replaced, @" {3,}", "  ").Trim();
            if (collapsed.Length > 0)
            {
                sb.AppendLine(collapsed);
            }
        }

        return sb.ToString();
    }
}
