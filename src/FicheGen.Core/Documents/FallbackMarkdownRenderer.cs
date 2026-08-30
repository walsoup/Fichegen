using System.Text.RegularExpressions;

namespace FicheGen.Core.Documents;

public static class FallbackMarkdownRenderer
{
    public static GeneratedDocument ConvertMarkdownToDocument(
        string rawContent,
        string title = "Document Généré",
        bool includeWarningCallout = false)
    {
        var blocks = new List<Block>();

        if (includeWarningCallout)
        {
            blocks.Add(new CalloutBoxBlock("warning", new List<Block>
            {
                new ParagraphBlock(new List<TextRun>
                {
                    new TextRun("Mode dégradé — Le modèle AI n'a pas renvoyé un format JSON valide. Le contenu a été converti depuis le format Markdown brut.", IsBold: true)
                })
            }));
        }

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return new GeneratedDocument(new DocumentMetadata(title), blocks, rawContent);
        }

        var lines = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var currentBulletItems = new List<List<TextRun>>();
        var currentNumberedItems = new List<List<TextRun>>();
        var currentTableLines = new List<string>();
        var currentKeyValuePairs = new List<KeyValuePair<string, string>>();

        void FlushBullets()
        {
            if (currentBulletItems.Count > 0)
            {
                blocks.Add(new BulletListBlock(new List<List<TextRun>>(currentBulletItems)));
                currentBulletItems.Clear();
            }
        }

        void FlushNumbered()
        {
            if (currentNumberedItems.Count > 0)
            {
                blocks.Add(new NumberedListBlock(new List<List<TextRun>>(currentNumberedItems)));
                currentNumberedItems.Clear();
            }
        }

        void FlushTable()
        {
            if (currentTableLines.Count > 0)
            {
                var tableBlock = ParseTable(currentTableLines);
                if (tableBlock != null)
                {
                    blocks.Add(tableBlock);
                }
                currentTableLines.Clear();
            }
        }

        void FlushKeyValue()
        {
            if (currentKeyValuePairs.Count > 0)
            {
                blocks.Add(new KeyValueGridBlock(new List<KeyValuePair<string, string>>(currentKeyValuePairs)));
                currentKeyValuePairs.Clear();
            }
        }

        void FlushAll()
        {
            FlushBullets();
            FlushNumbered();
            FlushTable();
            FlushKeyValue();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                FlushAll();
                continue;
            }

            // Page Break
            if (trimmed.Equals("[SAUT DE PAGE]", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("[PAGE BREAK]", StringComparison.OrdinalIgnoreCase))
            {
                FlushAll();
                blocks.Add(new PageBreakBlock());
                continue;
            }

            // Headings (# Heading)
            var headingMatch = Regex.Match(trimmed, @"^(#{1,6})\s+(.*)$");
            if (headingMatch.Success)
            {
                FlushAll();
                var level = headingMatch.Groups[1].Value.Length;
                var text = headingMatch.Groups[2].Value;
                blocks.Add(new HeadingBlock(level, ParseInlineRuns(text)));
                continue;
            }

            // Callout Box ([INFO], [ATTENTION], [CONSEIL], [OBJECTIF], [CORRIGE], etc.)
            var calloutMatch = Regex.Match(trimmed, @"^\[(INFO|CONSEIL|ATTENTION|WARNING|OBJECTIF|REMARQUE|ASTUCE|NOTE|IMPORTANT|CORRIG[EÉ]|DIFF[EÉ]RENCIATION)\]\s*(.*)$", RegexOptions.IgnoreCase);
            if (calloutMatch.Success)
            {
                FlushAll();
                var kind = calloutMatch.Groups[1].Value.ToLowerInvariant();
                var restOfLine = calloutMatch.Groups[2].Value.Trim();
                var calloutInnerBlocks = new List<Block>();
                if (!string.IsNullOrEmpty(restOfLine))
                {
                    calloutInnerBlocks.Add(new ParagraphBlock(ParseInlineRuns(restOfLine)));
                }

                while (i + 1 < lines.Length)
                {
                    var nextTrimmed = lines[i + 1].Trim();
                    if (string.IsNullOrEmpty(nextTrimmed) ||
                        Regex.IsMatch(nextTrimmed, @"^(#{1,6})\s+") ||
                        Regex.IsMatch(nextTrimmed, @"^\[(INFO|CONSEIL|ATTENTION|WARNING|OBJECTIF|REMARQUE|ASTUCE|NOTE|IMPORTANT|CORRIG[EÉ]|DIFF[EÉ]RENCIATION|SAUT DE PAGE|PAGE BREAK)\]", RegexOptions.IgnoreCase))
                    {
                        break;
                    }
                    i++;
                    var bulletM = Regex.Match(nextTrimmed, @"^[\*\-\+•]\s+(.*)$");
                    if (bulletM.Success)
                    {
                        calloutInnerBlocks.Add(new BulletListBlock(new List<List<TextRun>> { ParseInlineRuns(bulletM.Groups[1].Value) }));
                    }
                    else
                    {
                        calloutInnerBlocks.Add(new ParagraphBlock(ParseInlineRuns(nextTrimmed)));
                    }
                }

                blocks.Add(new CalloutBoxBlock(kind, calloutInnerBlocks.Count > 0 ? calloutInnerBlocks : new List<Block> { new ParagraphBlock(string.Empty) }));
                continue;
            }

            // Table lines (contains |)
            if (trimmed.Contains('|') && (trimmed.StartsWith("|") || trimmed.EndsWith("|") || trimmed.Count(c => c == '|') >= 2))
            {
                FlushBullets();
                FlushNumbered();
                currentTableLines.Add(trimmed);
                continue;
            }
            else if (currentTableLines.Count > 0)
            {
                FlushTable();
            }

            // Bullet list (*, -, +, •)
            var bulletMatch = Regex.Match(trimmed, @"^[\*\-\+•]\s+(.*)$");
            if (bulletMatch.Success)
            {
                FlushNumbered();
                FlushTable();
                currentBulletItems.Add(ParseInlineRuns(bulletMatch.Groups[1].Value));
                continue;
            }

            // Numbered list (1. item, 1) item)
            var numMatch = Regex.Match(trimmed, @"^\d+[\.\)]\s+(.*)$");
            if (numMatch.Success)
            {
                FlushBullets();
                FlushTable();
                currentNumberedItems.Add(ParseInlineRuns(numMatch.Groups[1].Value));
                continue;
            }

            // Key-Value line ("Durée : 50 min" or consecutive pairs)
            var kvMatch = Regex.Match(trimmed, @"^([^:\r\n\t]{2,40})\s*:\s*([^:\r\n\t]+)$");
            if (kvMatch.Success && !trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("[") && !trimmed.StartsWith("#"))
            {
                FlushBullets();
                FlushNumbered();
                FlushTable();
                currentKeyValuePairs.Add(new KeyValuePair<string, string>(kvMatch.Groups[1].Value.Trim(), kvMatch.Groups[2].Value.Trim()));
                continue;
            }
            else if (currentKeyValuePairs.Count > 0)
            {
                FlushKeyValue();
            }

            // Normal paragraph
            FlushAll();
            blocks.Add(new ParagraphBlock(ParseInlineRuns(trimmed)));
        }

        FlushAll();

        return new GeneratedDocument(new DocumentMetadata(title), blocks, rawContent);
    }

    private static TableBlock? ParseTable(List<string> tableLines)
    {
        if (tableLines.Count == 0) return null;

        var rows = new List<List<string>>();
        List<string>? headers = null;

        foreach (var rawLine in tableLines)
        {
            var line = rawLine.Trim();
            if (Regex.IsMatch(line, @"^\|?\s*[-:]+[-| :]*$"))
            {
                continue;
            }

            var parts = line.Split('|');
            var cells = new List<string>();
            for (int idx = 0; idx < parts.Length; idx++)
            {
                var c = parts[idx].Trim();
                if ((idx == 0 || idx == parts.Length - 1) && string.IsNullOrEmpty(c))
                    continue;
                cells.Add(c);
            }

            if (cells.Count > 0)
            {
                if (headers == null)
                {
                    headers = cells;
                }
                else
                {
                    rows.Add(cells);
                }
            }
        }

        if (headers == null) return null;

        return new TableBlock(headers, rows);
    }

    public static List<TextRun> ParseInlineRuns(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<TextRun> { new(string.Empty) };

        var runs = new List<TextRun>();
        var pattern = @"(\*\*\*.*?\*\*\*|\*\*.*?\*\*|__.*?__|\*.*?\*|_.*?_)";
        var parts = Regex.Split(text, pattern);

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if (part.StartsWith("***") && part.EndsWith("***") && part.Length > 6)
            {
                runs.Add(new TextRun(part.Substring(3, part.Length - 6), IsBold: true, IsItalic: true));
            }
            else if ((part.StartsWith("**") && part.EndsWith("**") && part.Length > 4) ||
                     (part.StartsWith("__") && part.EndsWith("__") && part.Length > 4))
            {
                runs.Add(new TextRun(part.Substring(2, part.Length - 4), IsBold: true));
            }
            else if ((part.StartsWith("*") && part.EndsWith("*") && part.Length > 2) ||
                     (part.StartsWith("_") && part.EndsWith("_") && part.Length > 2))
            {
                runs.Add(new TextRun(part.Substring(1, part.Length - 2), IsItalic: true));
            }
            else
            {
                runs.Add(new TextRun(part));
            }
        }

        return runs.Count > 0 ? runs : new List<TextRun> { new(text) };
    }
}
