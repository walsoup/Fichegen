using System.Text;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;

namespace FicheGen.Infrastructure.Export;

public sealed class RtfDocumentWriter : IRtfDocumentWriter
{
    public string ExportRtfString(GeneratedDocument doc, StylePreset? preset = null)
    {
        if (doc == null) return string.Empty;

        var sb = new StringBuilder();
        var activePreset = preset ?? StylePreset.Modern;

        // RTF Header
        sb.AppendLine(@"{\rtf1\ansi\deff0");
        sb.AppendLine(@"{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}{\f1\fnil\fcharset0 Georgia;}}");

        // Color table: 1=Primary, 2=Secondary, 3=Muted
        var (r1, g1, b1) = ParseHexColor(activePreset.PrimaryColor);
        var (r2, g2, b2) = ParseHexColor(activePreset.SecondaryColor);
        sb.AppendLine($@"{{\colortbl ;\red{r1}\green{g1}\blue{b1};\red{r2}\green{g2}\blue{b2};\red100\green116\blue139;}}");

        // Document Metadata Header
        if (!string.IsNullOrWhiteSpace(doc.Metadata.Title))
        {
            sb.AppendLine($@"\cf1\b\fs36 {EscapeRtf(doc.Metadata.Title)}\b0\cf0\par");
        }
        if (!string.IsNullOrWhiteSpace(doc.Metadata.Subtitle))
        {
            sb.AppendLine($@"\cf3\i\fs24 {EscapeRtf(doc.Metadata.Subtitle)}\i0\cf0\par");
        }

        sb.AppendLine(@"\par");

        // Document Body Blocks
        foreach (var block in doc.Blocks)
        {
            RenderBlockToRtf(block, sb);
            sb.AppendLine(@"\par");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    public byte[] ExportRtfBytes(GeneratedDocument doc, StylePreset? preset = null)
    {
        var rtfString = ExportRtfString(doc, preset);
        return Encoding.ASCII.GetBytes(rtfString);
    }

    private static void RenderBlockToRtf(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case HeadingBlock h:
                var fontSize = h.Level switch { 1 => 32, 2 => 28, _ => 24 };
                sb.Append($@"\cf1\b\fs{fontSize} ");
                foreach (var run in h.Runs)
                {
                    sb.Append(RenderRunRtf(run));
                }
                sb.AppendLine(@"\b0\cf0\par");
                break;

            case ParagraphBlock p:
                sb.Append(@"\fs22 ");
                foreach (var run in p.Runs)
                {
                    sb.Append(RenderRunRtf(run));
                }
                sb.AppendLine(@"\par");
                break;

            case BulletListBlock bl:
                foreach (var item in bl.Items)
                {
                    sb.Append(@"\bullet  ");
                    foreach (var run in item)
                    {
                        sb.Append(RenderRunRtf(run));
                    }
                    sb.AppendLine(@"\par");
                }
                break;

            case NumberedListBlock nl:
                for (int i = 0; i < nl.Items.Count; i++)
                {
                    sb.Append($@"\b {i + 1}.\b0  ");
                    foreach (var run in nl.Items[i])
                    {
                        sb.Append(RenderRunRtf(run));
                    }
                    sb.AppendLine(@"\par");
                }
                break;

            case TableBlock tbl:
                foreach (var row in tbl.Rows)
                {
                    sb.Append(@"\trowd\trgaph108");
                    int cellX = 2000;
                    for (int c = 0; c < row.Count; c++)
                    {
                        sb.Append($@"\cellx{cellX}");
                        cellX += 2000;
                    }
                    for (int c = 0; c < row.Count; c++)
                    {
                        sb.Append($@" {EscapeRtf(row[c])}\cell");
                    }
                    sb.AppendLine(@"\row");
                }
                break;

            case KeyValueGridBlock kv:
                foreach (var pair in kv.Pairs)
                {
                    sb.AppendLine($@"\cf3\b {EscapeRtf(pair.Key)} :\b0\cf0  {EscapeRtf(pair.Value)}\par");
                }
                break;

            case CalloutBoxBlock cb:
                sb.AppendLine(@"{\box\pad100");
                foreach (var inner in cb.ContentBlocks)
                {
                    RenderBlockToRtf(inner, sb);
                }
                sb.AppendLine("}");
                break;

            case PageBreakBlock:
                sb.AppendLine(@"\page");
                break;
        }
    }

    private static string RenderRunRtf(TextRun run)
    {
        var text = EscapeRtf(run.Text);
        if (run.IsBold) text = $@"\b {text}\b0 ";
        if (run.IsItalic) text = $@"\i {text}\i0 ";
        if (run.IsUnderline) text = $@"\ul {text}\ul0 ";
        return text;
    }

    private static string EscapeRtf(string str)
    {
        if (string.IsNullOrEmpty(str)) return string.Empty;

        var sb = new StringBuilder();
        foreach (char c in str)
        {
            if (c == '\\') sb.Append(@"\\");
            else if (c == '{') sb.Append(@"\{");
            else if (c == '}') sb.Append(@"\}");
            else if (c > 127)
            {
                sb.Append($@"\u{(int)c}?");
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return (15, 23, 42);
        var clean = hex.TrimStart('#');
        if (clean.Length == 6)
        {
            var r = Convert.ToByte(clean.Substring(0, 2), 16);
            var g = Convert.ToByte(clean.Substring(2, 2), 16);
            var b = Convert.ToByte(clean.Substring(4, 2), 16);
            return (r, g, b);
        }
        return (15, 23, 42);
    }
}
