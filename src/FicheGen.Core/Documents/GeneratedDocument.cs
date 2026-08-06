using System.Text;
using System.Text.Json.Serialization;

namespace FicheGen.Core.Documents;

public sealed record TextRun(
    string Text,
    bool IsBold = false,
    bool IsItalic = false,
    bool IsUnderline = false);

[JsonDerivedType(typeof(HeadingBlock), typeDiscriminator: "heading")]
[JsonDerivedType(typeof(ParagraphBlock), typeDiscriminator: "paragraph")]
[JsonDerivedType(typeof(BulletListBlock), typeDiscriminator: "bulletList")]
[JsonDerivedType(typeof(NumberedListBlock), typeDiscriminator: "numberedList")]
[JsonDerivedType(typeof(TableBlock), typeDiscriminator: "table")]
[JsonDerivedType(typeof(KeyValueGridBlock), typeDiscriminator: "keyValueGrid")]
[JsonDerivedType(typeof(CalloutBoxBlock), typeDiscriminator: "calloutBox")]
[JsonDerivedType(typeof(PageBreakBlock), typeDiscriminator: "pageBreak")]
public abstract record Block;

public sealed record HeadingBlock : Block
{
    public int Level { get; init; }
    public List<TextRun> Runs { get; init; }

    [JsonConstructor]
    public HeadingBlock(int level, List<TextRun> runs)
    {
        Level = level;
        Runs = runs ?? new List<TextRun>();
    }

    public HeadingBlock(int level, string text)
        : this(level, new List<TextRun> { new(text) })
    {
    }
}

public sealed record ParagraphBlock : Block
{
    public List<TextRun> Runs { get; init; }

    [JsonConstructor]
    public ParagraphBlock(List<TextRun> runs)
    {
        Runs = runs ?? new List<TextRun>();
    }

    public ParagraphBlock(string text)
        : this(new List<TextRun> { new(text) })
    {
    }
}

public sealed record BulletListBlock(List<List<TextRun>> Items) : Block;
public sealed record NumberedListBlock(List<List<TextRun>> Items) : Block;
public sealed record TableBlock(List<string> Headers, List<List<string>> Rows, List<double>? ColumnWidths = null) : Block;
public sealed record KeyValueGridBlock(List<KeyValuePair<string, string>> Pairs) : Block;
public sealed record CalloutBoxBlock(string Kind, List<Block> ContentBlocks) : Block;
public sealed record PageBreakBlock() : Block;

public sealed record DocumentMetadata(
    string Title,
    string? Subtitle = null,
    string? ClassLevel = null,
    string? Subject = null,
    int? Duration = null,
    string? Date = null,
    string DocType = "fiche");

public sealed record GeneratedDocument(
    DocumentMetadata Metadata,
    List<Block> Blocks,
    string? SourceJson = null)
{
    public string ToPlainText()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(Metadata.Title))
        {
            sb.AppendLine(Metadata.Title);
            sb.AppendLine(new string('=', Metadata.Title.Length));
        }

        if (!string.IsNullOrEmpty(Metadata.Subtitle))
        {
            sb.AppendLine(Metadata.Subtitle);
        }

        sb.AppendLine();

        foreach (var block in Blocks)
        {
            AppendBlockPlainText(block, sb);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendBlockPlainText(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case HeadingBlock h:
                sb.AppendLine($"{new string('#', Math.Clamp(h.Level, 1, 6))} {string.Join("", h.Runs.Select(r => r.Text))}");
                break;
            case ParagraphBlock p:
                sb.AppendLine(string.Join("", p.Runs.Select(r => r.Text)));
                break;
            case BulletListBlock bl:
                foreach (var item in bl.Items)
                {
                    sb.AppendLine($"• {string.Join("", item.Select(r => r.Text))}");
                }
                break;
            case NumberedListBlock nl:
                for (var i = 0; i < nl.Items.Count; i++)
                {
                    sb.AppendLine($"{i + 1}. {string.Join("", nl.Items[i].Select(r => r.Text))}");
                }
                break;
            case TableBlock tbl:
                if (tbl.Headers.Count > 0)
                {
                    sb.AppendLine(string.Join(" | ", tbl.Headers));
                }
                foreach (var row in tbl.Rows)
                {
                    sb.AppendLine(string.Join(" | ", row));
                }
                break;
            case KeyValueGridBlock kv:
                foreach (var pair in kv.Pairs)
                {
                    sb.AppendLine($"{pair.Key} : {pair.Value}");
                }
                break;
            case CalloutBoxBlock cb:
                sb.AppendLine($"[{cb.Kind.ToUpperInvariant()}]");
                foreach (var inner in cb.ContentBlocks)
                {
                    AppendBlockPlainText(inner, sb);
                }
                break;
        }
    }
}
