using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FicheGen.Core.Toc;

public static class TocParser
{
    // French educational heading: "Fiche n° 01 L'air, une source d'énergie 7"
    private static readonly Regex FrenchHeadingPattern = new(
        @"^(?<prefix>Fiche\s*(?:n[°o]|num[eé]ro)?\s*\d{1,3}|Le[cç]on\s*\d{1,3}|S[eé]ance\s*\d{1,3}|Chapitre\s*\d{1,3}|Module\s*\d{1,3}|Unit[eé]\s*\d{1,3}|Th[eè]me\s*\d{1,3}|Dossier\s*\d{1,3})\s*[:\-\—\–]?\s*(?<title>.{3,120}?)\s*(?:\.{2,}|…|[-_]{2,}|\s{1,})(?:(?:p\.|page)\s*)?(?<page>\d{1,4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Standard dot-leader / dash / underscore pattern
    private static readonly Regex DotLeadersPattern = new(
        @"^(?<title>.{3,120}?)\s*(?:\.{2,}|…|[-_]{2,})\s*(?:(?:p\.|page)\s*)?(?<page>\d{1,4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Space/column-separated pattern: "Title      14"
    private static readonly Regex ColumnSeparatedPattern = new(
        @"^(?<title>.{3,120}?)\s{2,}(?:(?:p\.|page)\s*)?(?<page>\d{1,4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Leading page number: "14 – Titre" or "p. 14 : Titre"
    private static readonly Regex LeadingPagePattern = new(
        @"^(?:(?:p\.|page)\s*)?(?<page>\d{1,4})\s*[:\.\-–—\s]\s*(?<title>.{3,120})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Multi-entry delimiter for lines containing multiple Fiche/Leçon definitions
    private static readonly Regex MultiEntrySplitter = new(
        @"(?<=\d{1,4})\s+(?=(?:Fiche\s*(?:n[°o]|num[eé]ro)?\s*\d{1,3}|Le[cç]on\s*\d{1,3}|S[eé]ance\s*\d{1,3}|Chapitre\s*\d{1,3}|Module\s*\d{1,3}|Unit[eé]\s*\d{1,3}))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex FilterKeywords = new(
        @"^\s*(?:SOMMAIRE|TABLE\s+DES\s+MATI[EÈ]RES|TABLE\s+DU\s+CONTENU|CONTENTS|INDEX|PAGE|TITRE)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FilterSentencePatterns = new(
        @"^(?:(?:[ÉEe]vitez|Demander|On trouve|Observez|Lisez|R[ée]pondez|Consigne|Attention|Ne pas|Il faut|Dans cette|Pour chaque|Voir mal|Avoir une|Avoir des|Tousser|Un m[ée]decin|Une auscultation)\b|.{0,10}\b(?:\d+\.\s*(?:avoir|suer|s'evanouir|une)))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<(string Title, int PrintedPage)> ParseToc(string tocPagesText)
    {
        if (string.IsNullOrWhiteSpace(tocPagesText))
            return Array.Empty<(string, int)>();

        var rawLines = SplitIntoLines(tocPagesText);

        // Try Dot Leaders first (highest precision for documents with explicit leaders)
        var entries = ExtractTier(rawLines, DotLeadersPattern);
        if (IsPlausibleToc(entries)) return entries;

        // Try French Heading Pattern (for fiches/leçons with whitespace separators)
        entries = ExtractTier(rawLines, FrenchHeadingPattern, isHeadingPattern: true);
        if (IsPlausibleToc(entries)) return entries;

        // Try Column Separated
        entries = ExtractTier(rawLines, ColumnSeparatedPattern);
        if (IsPlausibleToc(entries)) return entries;

        // Try Leading Page
        entries = ExtractTier(rawLines, LeadingPagePattern);
        if (IsPlausibleToc(entries)) return entries;

        return Array.Empty<(string, int)>();
    }

    private static bool IsPlausibleToc(IReadOnlyList<(string Title, int PrintedPage)> entries)
    {
        if (entries.Count < 2) return false;

        var backwardJumps = 0;
        for (int i = 1; i < entries.Count; i++)
        {
            if (entries[i].PrintedPage < entries[i - 1].PrintedPage)
            {
                backwardJumps++;
            }
        }

        // In a valid table of contents, page numbers almost always progress forward.
        // If more than 20% of entries jump backwards, this is random page text, not a TOC.
        return backwardJumps <= Math.Max(0, entries.Count / 5);
    }

    private static string[] SplitIntoLines(string text)
    {
        var normalized = NormalizeText(text);
        var initialLines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<string>();

        foreach (var line in initialLines)
        {
            // If the line has multiple entries joined together, split them
            var subLines = MultiEntrySplitter.Split(line);
            foreach (var sub in subLines)
            {
                var trimmed = sub.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    result.Add(trimmed);
                }
            }
        }

        return result.ToArray();
    }

    private static IReadOnlyList<(string Title, int PrintedPage)> ExtractTier(string[] lines, Regex pattern, bool isHeadingPattern = false)
    {
        var result = new List<(string Title, int PrintedPage)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var match = pattern.Match(line);
            if (!match.Success) continue;

            var title = match.Groups["title"].Value.Trim();
            var pageStr = match.Groups["page"].Value.Trim();

            if (isHeadingPattern && match.Groups["prefix"].Success && !string.IsNullOrWhiteSpace(match.Groups["prefix"].Value))
            {
                var prefix = match.Groups["prefix"].Value.Trim();
                if (!title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    title = $"{prefix} : {title}";
                }
            }

            // Clean title
            title = CleanTitle(title);
            if (title.Length < 3 || title.Length > 120) continue;
            if (FilterKeywords.IsMatch(title)) continue;
            if (FilterSentencePatterns.IsMatch(title)) continue;
            if (title.EndsWith('.') && !title.EndsWith("..")) continue;
            if (!int.TryParse(pageStr, out var printedPage) || printedPage <= 0 || printedPage > 2000) continue;

            var key = $"{title}|{printedPage}";
            if (seen.Add(key))
            {
                result.Add((title, printedPage));
            }
        }

        return result;
    }

    private static string CleanTitle(string title)
    {
        var cleaned = title.Trim();
        // Remove trailing dots, dashes, underscores
        cleaned = Regex.Replace(cleaned, @"[\.\-_…]+$", "").Trim();
        // Clean multiple spaces
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");
        return cleaned;
    }

    private static string NormalizeText(string text)
    {
        var formC = text.Normalize(NormalizationForm.FormC);
        var lines = formC.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            var replaced = line.Replace("\t", "  ").Replace("…", "..");
            var collapsed = Regex.Replace(replaced, @" {3,}", "  ").Trim();
            if (collapsed.Length > 0)
            {
                sb.AppendLine(collapsed);
            }
        }

        return sb.ToString();
    }
}

