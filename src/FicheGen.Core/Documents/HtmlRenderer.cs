using System.Text;

namespace FicheGen.Core.Documents;

public static class HtmlRenderer
{
    public static string RenderToFragment(GeneratedDocument doc, bool isStudentVersion = false)
    {
        if (doc == null) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("<article class=\"fiche-content\">");

        // Header Metadata
        sb.AppendLine("  <header class=\"document-header\">");
        if (!string.IsNullOrWhiteSpace(doc.Metadata.Title))
        {
            var titleSuffix = isStudentVersion ? " — Version Élève" : string.Empty;
            sb.AppendLine($"    <h1 class=\"doc-title\">{EncodeText(doc.Metadata.Title + titleSuffix)}</h1>");
        }
        if (!string.IsNullOrWhiteSpace(doc.Metadata.Subtitle))
        {
            sb.AppendLine($"    <p class=\"doc-subtitle\">{EncodeText(doc.Metadata.Subtitle)}</p>");
        }

        var metaBadges = new List<string>();
        if (!string.IsNullOrWhiteSpace(doc.Metadata.ClassLevel))
            metaBadges.Add($"<span class=\"badge level-badge\">Niveau: {EncodeText(doc.Metadata.ClassLevel)}</span>");
        if (!string.IsNullOrWhiteSpace(doc.Metadata.Subject))
            metaBadges.Add($"<span class=\"badge subject-badge\">Discipline: {EncodeText(doc.Metadata.Subject)}</span>");
        if (doc.Metadata.Duration.HasValue && doc.Metadata.Duration.Value > 0)
            metaBadges.Add($"<span class=\"badge duration-badge\">Durée: {doc.Metadata.Duration.Value} min</span>");
        if (!string.IsNullOrWhiteSpace(doc.Metadata.Date))
            metaBadges.Add($"<span class=\"badge date-badge\">{EncodeText(doc.Metadata.Date)}</span>");

        if (metaBadges.Count > 0)
        {
            sb.AppendLine("    <div class=\"metadata-bar\">");
            foreach (var badge in metaBadges)
            {
                sb.AppendLine($"      {badge}");
            }
            sb.AppendLine("    </div>");
        }

        if (isStudentVersion)
        {
            sb.AppendLine("    <div class=\"student-header-box\">");
            sb.AppendLine("      <div><strong>Nom :</strong> .......................................... &nbsp;&nbsp; <strong>Prénom :</strong> ..........................................</div>");
            sb.AppendLine("      <div><strong>Classe :</strong> ................ &nbsp;&nbsp; <strong>Note :</strong> ..... / 20</div>");
            sb.AppendLine("    </div>");
        }

        sb.AppendLine("  </header>");

        // Document Body Blocks
        sb.AppendLine("  <main class=\"document-body\">");
        var skippingCorrectionSection = false;
        foreach (var block in doc.Blocks)
        {
            if (isStudentVersion)
            {
                if (block is HeadingBlock hb)
                {
                    var headingText = string.Concat(hb.Runs.Select(r => r.Text)).ToLowerInvariant();
                    if (headingText.Contains("corrigé") || headingText.Contains("correction") || headingText.Contains("solutions"))
                    {
                        skippingCorrectionSection = true;
                        continue;
                    }
                    else
                    {
                        skippingCorrectionSection = false;
                    }
                }

                if (skippingCorrectionSection)
                {
                    continue;
                }

                if (block is CalloutBoxBlock cb)
                {
                    var kind = cb.Kind.ToLowerInvariant();
                    if (kind.Contains("corrige") || kind.Contains("correction") || kind.Contains("solution") || kind.Contains("reponse"))
                    {
                        // Remplacer le corrigé par une zone de réponse élève pointillée
                        sb.AppendLine("    <div class=\"student-answer-box\">");
                        sb.AppendLine("      <div class=\"dots-line\"></div>");
                        sb.AppendLine("      <div class=\"dots-line\"></div>");
                        sb.AppendLine("      <div class=\"dots-line\"></div>");
                        sb.AppendLine("    </div>");
                        continue;
                    }
                }
            }

            RenderBlock(block, sb, indent: "    ");
        }
        sb.AppendLine("  </main>");

        sb.AppendLine("</article>");
        return sb.ToString();
    }

    public static string RenderToHtml(GeneratedDocument doc, StylePreset? preset = null, bool isStudentVersion = false)
    {
        var css = preset?.CustomCss ?? DefaultCss;
        return RenderToFullHtml(doc, css, isStudentVersion);
    }

    public static string RenderToFullHtml(GeneratedDocument doc, string? customCss = null, bool isStudentVersion = false)
    {
        var fragment = RenderToFragment(doc, isStudentVersion);

        var css = CssSanitizer.SanitizeCss(customCss ?? DefaultCss);

        return $@"<!DOCTYPE html>
<html lang=""fr"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""Content-Security-Policy"" content=""default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src 'self' https: data:;"">
    <title>{EncodeText(doc.Metadata.Title)}</title>
    <style>
{css}
    </style>
</head>
<body>
{fragment}
</body>
</html>";
    }

    private static void RenderBlock(Block block, StringBuilder sb, string indent)
    {
        switch (block)
        {
            case HeadingBlock h:
                var tag = $"h{Math.Clamp(h.Level, 1, 6)}";
                sb.AppendLine($"{indent}<{tag}>{RenderRuns(h.Runs)}</{tag}>");
                break;

            case ParagraphBlock p:
                sb.AppendLine($"{indent}<p>{RenderRuns(p.Runs)}</p>");
                break;

            case BulletListBlock bl:
                sb.AppendLine($"{indent}<ul>");
                foreach (var item in bl.Items)
                {
                    sb.AppendLine($"{indent}  <li>{RenderRuns(item)}</li>");
                }
                sb.AppendLine($"{indent}</ul>");
                break;

            case NumberedListBlock nl:
                sb.AppendLine($"{indent}<ol>");
                foreach (var item in nl.Items)
                {
                    sb.AppendLine($"{indent}  <li>{RenderRuns(item)}</li>");
                }
                sb.AppendLine($"{indent}</ol>");
                break;

            case TableBlock tbl:
                sb.AppendLine($"{indent}<div class=\"table-responsive\">");
                sb.AppendLine($"{indent}  <table>");
                if (tbl.Headers.Count > 0)
                {
                    sb.AppendLine($"{indent}    <thead>");
                    sb.AppendLine($"{indent}      <tr>");
                    foreach (var h in tbl.Headers)
                    {
                        sb.AppendLine($"{indent}        <th>{EncodeText(h)}</th>");
                    }
                    sb.AppendLine($"{indent}      </tr>");
                    sb.AppendLine($"{indent}    </thead>");
                }
                sb.AppendLine($"{indent}    <tbody>");
                foreach (var row in tbl.Rows)
                {
                    sb.AppendLine($"{indent}      <tr>");
                    foreach (var cell in row)
                    {
                        sb.AppendLine($"{indent}        <td>{EncodeText(cell)}</td>");
                    }
                    sb.AppendLine($"{indent}      </tr>");
                }
                sb.AppendLine($"{indent}    </tbody>");
                sb.AppendLine($"{indent}  </table>");
                sb.AppendLine($"{indent}</div>");
                break;

            case KeyValueGridBlock kv:
                sb.AppendLine($"{indent}<div class=\"key-value-grid\">");
                foreach (var pair in kv.Pairs)
                {
                    sb.AppendLine($"{indent}  <div class=\"kv-item\">");
                    sb.AppendLine($"{indent}    <span class=\"kv-key\">{EncodeText(pair.Key)}</span>");
                    sb.AppendLine($"{indent}    <span class=\"kv-value\">{EncodeText(pair.Value)}</span>");
                    sb.AppendLine($"{indent}  </div>");
                }
                sb.AppendLine($"{indent}</div>");
                break;

            case CalloutBoxBlock cb:
                var kindClass = EncodeText(cb.Kind.ToLowerInvariant());
                sb.AppendLine($"{indent}<div class=\"callout callout-{kindClass}\">");
                foreach (var inner in cb.ContentBlocks)
                {
                    RenderBlock(inner, sb, indent + "  ");
                }
                sb.AppendLine($"{indent}</div>");
                break;

            case PageBreakBlock:
                sb.AppendLine($"{indent}<div class=\"page-break\"></div>");
                break;
        }
    }

    private static string RenderRuns(IReadOnlyList<TextRun> runs)
    {
        var sb = new StringBuilder();
        foreach (var run in runs)
        {
            var text = EncodeText(run.Text);
            if (run.IsBold) text = $"<strong>{text}</strong>";
            if (run.IsItalic) text = $"<em>{text}</em>";
            if (run.IsUnderline) text = $"<u>{text}</u>";
            sb.Append(text);
        }
        return sb.ToString();
    }

    private static string EncodeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    public const string DefaultCss = @"
        :root {
            --primary: #0f172a;
            --accent: #2563eb;
            --bg: #ffffff;
            --text: #1e293b;
            --border: #e2e8f0;
            --muted: #64748b;
        }
        body {
            font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
            background: var(--bg);
            color: var(--text);
            line-height: 1.6;
            margin: 0;
            padding: 2rem;
        }
        .fiche-content {
            max-width: 800px;
            margin: 0 auto;
        }
        .doc-title {
            color: var(--primary);
            font-size: 2rem;
            margin-bottom: 0.25rem;
            border-bottom: 3px solid var(--accent);
            padding-bottom: 0.5rem;
        }
        .doc-subtitle {
            color: var(--muted);
            font-size: 1.1rem;
            margin-top: 0;
        }
        .metadata-bar {
            display: flex;
            gap: 0.5rem;
            flex-wrap: wrap;
            margin-bottom: 1.5rem;
        }
        .badge {
            background: #f1f5f9;
            color: #475569;
            padding: 0.25rem 0.6rem;
            border-radius: 9999px;
            font-size: 0.85rem;
            font-weight: 500;
        }
        .table-responsive {
            overflow-x: auto;
            margin: 1rem 0;
        }
        table {
            width: 100%;
            border-collapse: collapse;
            margin: 1rem 0;
        }
        th, td {
            border: 1px solid var(--border);
            padding: 0.6rem 0.8rem;
            text-align: left;
        }
        th {
            background: #f8fafc;
            font-weight: 600;
        }
        .key-value-grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
            gap: 0.75rem;
            background: #f8fafc;
            padding: 1rem;
            border-radius: 8px;
            border: 1px solid var(--border);
            margin: 1rem 0;
        }
        .kv-key {
            font-weight: 600;
            display: block;
            font-size: 0.85rem;
            color: var(--muted);
        }
        .callout {
            border-left: 4px solid var(--accent);
            background: #f0f9ff;
            padding: 1rem;
            border-radius: 0 8px 8px 0;
            margin: 1rem 0;
        }
        .callout-corrige {
            border-left-color: #16a34a;
            background: #f0fdf4;
        }
        .callout-differentiation {
            border-left-color: #9333ea;
            background: #faf5ff;
        }
        .callout-warning {
            border-left-color: #ea580c;
            background: #fff7ed;
        }
        .page-break {
            page-break-before: always;
            border-top: 1px dashed var(--border);
            margin: 2rem 0;
        }
        .student-header-box {
            display: flex;
            justify-content: space-between;
            align-items: center;
            background: #f8fafc;
            border: 1px dashed #94a3b8;
            border-radius: 8px;
            padding: 12px 16px;
            margin: 1rem 0 1.5rem 0;
            font-size: 0.9rem;
            color: #334155;
        }
        .student-answer-box {
            margin: 12px 0 18px 0;
            padding: 10px 14px;
            border: 1px dashed #cbd5e1;
            border-radius: 6px;
            background: #fafafa;
        }
        .dots-line {
            border-bottom: 1px dotted #94a3b8;
            height: 24px;
        }
    ";
}
