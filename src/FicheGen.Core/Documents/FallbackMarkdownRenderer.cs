using System.Text.RegularExpressions;

namespace FicheGen.Core.Documents;

public static class FallbackMarkdownRenderer
{
    public static GeneratedDocument ConvertMarkdownToDocument(string rawContent, string title = "Document Généré")
    {
        var blocks = new List<Block>
        {
            new CalloutBoxBlock("warning", new List<Block>
            {
                new ParagraphBlock(new List<TextRun>
                {
                    new TextRun("Mode dégradé — Le modèle AI n'a pas renvoyé un format JSON valide. Le contenu a été converti depuis le format Markdown brut.", IsBold: true)
                })
            })
        };

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return new GeneratedDocument(new DocumentMetadata(title), blocks, rawContent);
        }

        var lines = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var currentListItems = new List<List<TextRun>>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                FlushList(currentListItems, blocks);
                continue;
            }

            // Headings
            var headingMatch = Regex.Match(trimmed, @"^(#{1,6})\s+(.*)$");
            if (headingMatch.Success)
            {
                FlushList(currentListItems, blocks);
                var level = headingMatch.Groups[1].Value.Length;
                var text = headingMatch.Groups[2].Value;
                blocks.Add(new HeadingBlock(level, ParseInlineRuns(text)));
                continue;
            }

            // Bullet list
            var bulletMatch = Regex.Match(trimmed, @"^[\*\-\+]\s+(.*)$");
            if (bulletMatch.Success)
            {
                currentListItems.Add(ParseInlineRuns(bulletMatch.Groups[1].Value));
                continue;
            }

            // Normal paragraph
            FlushList(currentListItems, blocks);
            blocks.Add(new ParagraphBlock(ParseInlineRuns(trimmed)));
        }

        FlushList(currentListItems, blocks);

        return new GeneratedDocument(new DocumentMetadata(title), blocks, rawContent);
    }

    private static void FlushList(List<List<TextRun>> currentListItems, List<Block> blocks)
    {
        if (currentListItems.Count > 0)
        {
            blocks.Add(new BulletListBlock(new List<List<TextRun>>(currentListItems)));
            currentListItems.Clear();
        }
    }

    private static List<TextRun> ParseInlineRuns(string text)
    {
        var runs = new List<TextRun>();

        // Basic bold **text** parsing
        var parts = Regex.Split(text, @"(\*\*.*?\*\*)");
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
            {
                runs.Add(new TextRun(part.Substring(2, part.Length - 4), IsBold: true));
            }
            else
            {
                runs.Add(new TextRun(part));
            }
        }

        return runs;
    }
}
