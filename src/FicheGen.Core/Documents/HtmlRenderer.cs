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
        sb.AppendLine("    <div class=\"doc-kicker\">Fiche Pédagogique</div>");
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
        var css = BuildPresetCss(preset);
        return RenderToFullHtml(doc, css, isStudentVersion);
    }

    /// <summary>
    /// Construit la feuille de style complète du document à partir des propriétés
    /// du préréglage : couleurs, typographie, marges, arrondis et traitement
    /// d'en-tête (<see cref="StylePreset.HeaderLayout"/>). La feuille personnalisée
    /// du préréglage, si présente, est ajoutée à la fin et prime sur la base.
    /// Sans préréglage, le thème « Moderne » par défaut s'applique.
    /// </summary>
    public static string BuildPresetCss(StylePreset? preset)
    {
        var p = preset ?? StylePreset.Modern;
        var primary = p.PrimaryColor;
        var secondary = p.SecondaryColor;
        var accent = string.IsNullOrWhiteSpace(p.AccentColor) ? secondary : p.AccentColor;
        var font = p.FontFamily;
        var radius = Math.Clamp(p.CornerRadiusPx, 0, 28)
            .ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";

        var top = Math.Clamp(p.MarginMm, 5, 40);
        var bottom = Math.Clamp(p.MarginBottomMm ?? top, 5, 40);
        var left = Math.Clamp(p.MarginLeftMm ?? top, 5, 40);
        var right = Math.Clamp(p.MarginRightMm ?? top, 5, 40);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var margin = $"{top.ToString(inv)}mm {right.ToString(inv)}mm {bottom.ToString(inv)}mm {left.ToString(inv)}mm";

        var css = new StringBuilder(8192);
        css.AppendLine(ApplyTokens(SharedCss, primary, secondary, accent, font, margin, radius));

        css.AppendLine(ApplyTokens(HeaderLayoutCss(p.HeaderLayout), primary, secondary, accent, font, margin, radius));

        var extras = LayoutExtrasCss(p.HeaderLayout);
        if (extras.Length > 0)
        {
            css.AppendLine(ApplyTokens(extras, primary, secondary, accent, font, margin, radius));
        }

        var custom = p.CustomCss;
        if (!string.IsNullOrWhiteSpace(custom))
        {
            css.AppendLine("/* — Style personnalisé — */");
            css.AppendLine(custom);
        }

        return css.ToString();
    }

    private static string ApplyTokens(string template, string primary, string secondary, string accent, string font, string margin, string radius) =>
        template
            .Replace("__PRIMARY__", primary)
            .Replace("__SECONDARY__", secondary)
            .Replace("__ACCENT__", accent)
            .Replace("__FONT__", font)
            .Replace("__MARGIN__", margin)
            .Replace("__RADIUS__", radius);

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

    // ═════════════════════════════════════════════════════════════════════
    //  Moteur de feuilles de style par préréglage
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>CSS commun à tous les traitements d'en-tête.</summary>
    private const string SharedCss = @"
        :root {
            --primary: __PRIMARY__;
            --secondary: __SECONDARY__;
            --accent: __ACCENT__;
            --font-family: __FONT__;
            --page-margin: __MARGIN__;
            --radius: __RADIUS__;
            --text-main: #1e293b;
            --text-muted: #64748b;
            --border-light: #e2e8f0;
            --surface-subtle: #f8fafc;
        }
        * { box-sizing: border-box; }
        html {
            -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
            text-rendering: optimizeLegibility;
            -webkit-font-smoothing: antialiased;
            -moz-osx-font-smoothing: grayscale;
        }
        body {
            font-family: var(--font-family);
            background: #ffffff;
            color: var(--text-main);
            font-size: 14px;
            line-height: 1.65;
            margin: 0;
            padding: clamp(16px, 2.5vw, 28px);
        }
        .fiche-content {
            max-width: 820px;
            margin: 0 auto;
        }

        /* ── En-tête : Kicker & Titres ────────────────────────────────────────── */
        .doc-kicker {
            font-size: 0.68rem;
            font-weight: 800;
            letter-spacing: 0.16em;
            text-transform: uppercase;
            color: var(--secondary);
            margin-bottom: 4px;
        }
        h1, h2, h3, h4 { color: var(--primary); line-height: 1.25; margin: 1.5em 0 0.5em; text-wrap: balance; }
        h1 { font-size: 1.35rem; font-weight: 800; letter-spacing: -0.02em; }
        h2 { font-size: 1.10rem; font-weight: 700; letter-spacing: -0.01em; border-bottom: 1px solid rgba(0, 0, 0, 0.06); padding-bottom: 4px; }
        h3 { font-size: 0.96rem; font-weight: 650; color: var(--secondary); margin-top: 1.15em; }
        h4 { font-size: 0.88rem; font-weight: 650; color: var(--text-muted); }
        p { margin: 0.55em 0; color: #334155; line-height: 1.62; }
        ul, ol { margin: 0.55em 0; padding-left: 1.35em; color: #334155; }
        li { margin: 0.35em 0; line-height: 1.55; }
        li::marker { color: var(--secondary); font-weight: 700; }
        strong { font-weight: 700; color: #0f172a; }
        em { font-style: italic; color: #334155; }
        a { color: var(--secondary); text-decoration-thickness: from-font; text-underline-offset: 2px; }

        /* ── Tableaux éditoriaux haut de gamme ───────────────────────────────── */
        .table-responsive {
            overflow-x: auto;
            margin: 1.4rem 0;
            border-radius: var(--radius);
            border: 1px solid var(--border-light);
            box-shadow: 0 1px 3px rgba(0, 0, 0, 0.02);
            background: #ffffff;
        }
        table {
            width: 100%;
            border-collapse: separate;
            border-spacing: 0;
            font-size: 0.88rem;
        }
        th, td {
            padding: 10px 14px;
            text-align: left;
            vertical-align: top;
        }
        th {
            background: #f8fafc;
            color: #1e293b;
            font-weight: 750;
            font-size: 0.74rem;
            text-transform: uppercase;
            letter-spacing: 0.07em;
            border-bottom: 2px solid var(--primary);
        }
        td {
            border-bottom: 1px solid var(--border-light);
            color: #334155;
            line-height: 1.5;
        }
        tr:last-child td { border-bottom: none; }
        tbody tr:nth-child(even) td { background: #fbfcfe; }
        tbody tr:hover td { background: #f8fafc; }
        tbody td:first-child { font-weight: 650; color: #1e293b; }

        /* ── Grilles clés / valeurs (Bento cards) ────────────────────────────── */
        .key-value-grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
            gap: 12px;
            margin: 1.4rem 0;
        }
        .kv-item {
            background: var(--surface-subtle);
            border: 1px solid var(--border-light);
            border-radius: var(--radius);
            padding: 10px 14px;
            box-shadow: 0 1px 2px rgba(0, 0, 0, 0.02);
        }
        .kv-key {
            display: block;
            font-weight: 800;
            font-size: 0.68rem;
            text-transform: uppercase;
            letter-spacing: 0.10em;
            color: var(--secondary);
            margin-bottom: 3px;
        }
        .kv-value {
            display: block;
            font-size: 0.90rem;
            color: #1e293b;
            font-weight: 600;
            line-height: 1.4;
        }

        /* ── Encadrés pédagogiques signature ─────────────────────────────────── */
        .callout {
            background: #fafafa;
            border: 1px solid var(--border-light);
            border-left: 4px solid var(--accent);
            padding: 14px 18px;
            border-radius: var(--radius);
            margin: 1.4rem 0;
            box-shadow: 0 1px 3px rgba(0, 0, 0, 0.02);
        }
        .callout p { margin: 0.25em 0; line-height: 1.55; color: inherit; }
        .callout ul, .callout ol { margin: 0.35em 0; padding-left: 1.25em; color: inherit; }
        .callout-objectifs {
            background: #f8fafc;
            border-color: #cbd5e1;
            border-left: 4px solid var(--primary);
        }
        .callout-objectifs::before {
            content: 'OBJECTIFS D\'APPRENTISSAGE';
            display: inline-block;
            font-weight: 800;
            font-size: 0.66rem;
            letter-spacing: 0.10em;
            text-transform: uppercase;
            color: var(--primary);
            background: rgba(30, 58, 138, 0.08);
            padding: 2px 8px;
            border-radius: 4px;
            margin-bottom: 8px;
        }
        .callout-corrige, .callout-correction, .callout-solutions {
            background: #f0fdf4;
            border-color: #bbf7d0;
            border-left: 4px solid #16a34a;
        }
        .callout-corrige::before, .callout-correction::before, .callout-solutions::before {
            content: 'CORRIGÉ & ÉLÉMENTS DE RÉPONSE';
            display: inline-block;
            font-weight: 800;
            font-size: 0.66rem;
            letter-spacing: 0.10em;
            text-transform: uppercase;
            color: #15803d;
            background: #dcfce7;
            padding: 2px 8px;
            border-radius: 4px;
            margin-bottom: 8px;
        }
        .callout-differentiation, .callout-aide, .callout-dys {
            background: #faf5ff;
            border-color: #e9d5ff;
            border-left: 4px solid #9333ea;
        }
        .callout-differentiation::before, .callout-aide::before, .callout-dys::before {
            content: 'DIFFÉRENCIATION PÉDAGOGIQUE';
            display: inline-block;
            font-weight: 800;
            font-size: 0.66rem;
            letter-spacing: 0.10em;
            text-transform: uppercase;
            color: #7e22ce;
            background: #f3e8ff;
            padding: 2px 8px;
            border-radius: 4px;
            margin-bottom: 8px;
        }
        .callout-warning, .callout-vigilance, .callout-attention {
            background: #fffbeb;
            border-color: #fde68a;
            border-left: 4px solid #f59e0b;
        }
        .callout-warning::before, .callout-vigilance::before, .callout-attention::before {
            content: 'POINT DE VIGILANCE';
            display: inline-block;
            font-weight: 800;
            font-size: 0.66rem;
            letter-spacing: 0.10em;
            text-transform: uppercase;
            color: #b45309;
            background: #fef3c7;
            padding: 2px 8px;
            border-radius: 4px;
            margin-bottom: 8px;
        }

        /* ── Version élève ─────────────────────────────────────────��─── */
        .student-header-box {
            display: grid;
            grid-template-columns: 1fr auto;
            gap: 12px;
            background: #fafafa;
            border: 1.5px dashed #cbd5e1;
            border-radius: var(--radius);
            padding: 14px 18px;
            margin-top: 14px;
            font-size: 0.88rem;
            color: #1e293b;
        }
        .student-answer-box {
            margin: 14px 0 20px 0;
            padding: 14px 16px;
            border: 1.5px dashed #cbd5e1;
            border-radius: var(--radius);
            background: #fdfdfd;
        }
        .dots-line {
            border-bottom: 1.5px dotted #94a3b8;
            height: 28px;
        }

        /* ── Sauts de page & impression ──────────────────────────────── */
        .page-break {
            page-break-before: always;
            border-top: 1px dashed var(--border-light);
            margin: 2.5rem 0;
        }
        @media print {
            body { padding: 0; font-size: 12px; }
            .fiche-content { max-width: 100%; margin: 0; }
            .document-header, .callout, th, .badge, .kv-item { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
            h1, h2, h3, .callout, table, .key-value-grid { break-inside: avoid; }
        }
    ";

    /// <summary>Traitement d'en-tête selon <see cref="StylePreset.HeaderLayout"/>.
    /// NB : pas de <c>position: absolute</c> — le sanitiseur CSS l'interdit.</summary>
    private static string HeaderLayoutCss(string? layout) => layout switch
    {
        StylePreset.HeaderBand => @"
        /* ── En-tête : bandeau chaleureux d'école primaire ───────────── */
        .document-header {
            background: linear-gradient(135deg, #f8f7ff 0%, #fdf4ff 100%);
            border: 1.5px solid #e0e7ff;
            border-radius: 14px;
            padding: 16px 18px;
            margin-bottom: 18px;
            box-shadow: 0 2px 8px rgba(99, 102, 241, 0.05);
        }
        .doc-title {
            color: var(--primary);
            font-size: 1.50rem;
            font-weight: 850;
            line-height: 1.2;
            letter-spacing: -0.015em;
            margin: 0 0 4px;
        }
        .doc-subtitle {
            color: #4b5563;
            font-size: 0.92rem;
            font-weight: 550;
            margin: 0 0 12px;
            line-height: 1.35;
        }
        .metadata-bar { display: flex; gap: 6px; flex-wrap: wrap; align-items: center; }
        .badge {
            display: inline-flex;
            align-items: center;
            background: #ffffff;
            border: 1.5px solid #e0e7ff;
            color: #4338ca;
            padding: 3px 10px;
            border-radius: 999px;
            font-size: 0.72rem;
            font-weight: 750;
            white-space: nowrap;
            box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
        }
        .badge.level-badge { background: #e0f2fe; border-color: #bae6fd; color: #0369a1; }
        .badge.subject-badge { background: #fef3c7; border-color: #fde68a; color: #b45309; }
        .badge.duration-badge { background: #dcfce7; border-color: #bbf7d0; color: #15803d; }
        .badge.date-badge { background: #fce7f3; border-color: #fbcfe8; color: #be185d; }
    ",
        StylePreset.HeaderCentered => @"
        /* ── En-tête : titre centré, filet double, prestige éditorial ── */
        .document-header {
            text-align: center;
            border-bottom: 3px double var(--primary);
            padding-bottom: 18px;
            margin-bottom: 24px;
        }
        .doc-kicker {
            font-family: Georgia, serif;
            font-variant: small-caps;
            letter-spacing: 0.18em;
            font-size: 0.72rem;
            color: #854d0e;
            font-weight: 700;
            margin-bottom: 4px;
        }
        .doc-title {
            font-family: Georgia, 'Times New Roman', serif;
            color: var(--primary);
            font-size: 1.70rem;
            font-weight: 700;
            line-height: 1.25;
            letter-spacing: 0.01em;
            margin: 0 0 6px;
        }
        .doc-subtitle {
            font-family: Georgia, serif;
            color: #475569;
            font-style: italic;
            font-size: 0.98rem;
            margin: 0 0 14px;
        }
        .metadata-bar {
            display: inline-flex;
            gap: 8px;
            flex-wrap: wrap;
            justify-content: center;
            align-items: center;
            padding: 5px 14px;
            border-top: 1px solid #cbd5e1;
            border-bottom: 1px solid #cbd5e1;
        }
        .badge {
            display: inline-flex;
            align-items: center;
            background: transparent;
            border: none;
            font-family: Georgia, serif;
            color: #475569;
            padding: 2px 4px;
            font-size: 0.78rem;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.04em;
            font-variant: small-caps;
            white-space: nowrap;
        }
        .badge + .badge::before {
            content: '◆';
            font-size: 0.45rem;
            color: #b91c1c;
            margin-right: 8px;
            vertical-align: 1px;
        }
    ",
        StylePreset.HeaderMinimal => @"
        /* ── En-tête : minimaliste, typographie suisse et pastille ───── */
        .document-header {
            border-bottom: 2px solid var(--text-main);
            padding-bottom: 14px;
            margin-bottom: 22px;
        }
        .doc-kicker {
            font-size: 0.65rem;
            font-weight: 850;
            letter-spacing: 0.20em;
            text-transform: uppercase;
            color: #71717a;
            margin-bottom: 4px;
        }
        .doc-title {
            color: var(--text-main);
            font-size: 1.60rem;
            font-weight: 900;
            letter-spacing: -0.04em;
            line-height: 1.15;
            margin: 0 0 4px;
        }
        .doc-title::before {
            content: '';
            display: inline-block;
            width: 8px;
            height: 8px;
            background: var(--accent);
            border-radius: 2px;
            margin-right: 8px;
            vertical-align: 1px;
        }
        .doc-subtitle {
            color: var(--text-muted);
            font-size: 0.90rem;
            font-weight: 450;
            margin: 0 0 10px;
        }
        .metadata-bar { display: flex; flex-wrap: wrap; align-items: center; gap: 0; }
        .badge {
            display: inline-flex;
            align-items: center;
            background: transparent;
            border: none;
            color: var(--text-muted);
            padding: 0 8px;
            font-size: 0.72rem;
            font-weight: 750;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            white-space: nowrap;
        }
        .metadata-bar .badge:first-child { padding-left: 0; }
        .metadata-bar .badge + .badge { border-left: 1px solid #cbd5e1; }
    ",
        _ => @"
        /* ── En-tête : filet bicolore contemporain ───────────────────── */
        .document-header {
            border-bottom: 2.5px solid var(--primary);
            padding-bottom: 14px;
            margin-bottom: 22px;
        }
        .doc-kicker {
            font-size: 0.68rem;
            font-weight: 800;
            letter-spacing: 0.16em;
            text-transform: uppercase;
            color: var(--secondary);
            margin-bottom: 4px;
        }
        .doc-title {
            color: var(--primary);
            font-size: 1.65rem;
            font-weight: 850;
            line-height: 1.20;
            letter-spacing: -0.03em;
            margin: 0 0 4px;
        }
        .doc-subtitle {
            color: #64748b;
            font-size: 0.95rem;
            font-weight: 450;
            margin: 0 0 12px;
            line-height: 1.4;
        }
        .metadata-bar { display: flex; gap: 6px; flex-wrap: wrap; align-items: center; }
        .badge {
            display: inline-flex;
            align-items: center;
            background: #f1f5f9;
            border: 1px solid #e2e8f0;
            color: #334155;
            padding: 3px 10px;
            border-radius: 6px;
            font-size: 0.74rem;
            font-weight: 650;
            white-space: nowrap;
        }
        .badge.level-badge {
            background: rgba(30, 58, 138, 0.08);
            border-color: rgba(30, 58, 138, 0.22);
            color: var(--primary);
            font-weight: 750;
        }
        .badge.subject-badge {
            background: rgba(217, 119, 6, 0.08);
            border-color: rgba(217, 119, 6, 0.25);
            color: #92400e;
            font-weight: 750;
        }
        .badge.duration-badge {
            background: rgba(16, 185, 129, 0.08);
            border-color: rgba(16, 185, 129, 0.25);
            color: #047857;
            font-weight: 750;
        }
        .badge.date-badge {
            background: #f8fafc;
            border-color: #e2e8f0;
            color: #64748b;
        }
    "
    };

    /// <summary>Ajustements de composants propres à chaque traitement.</summary>
    private static string LayoutExtrasCss(string? layout) => layout switch
    {
        StylePreset.HeaderMinimal => @"
        /* Style minimaliste suisse : numérotation nette, tables épurées. */
        h1:not(.doc-title) { font-size: 1.20rem; font-weight: 900; letter-spacing: -0.025em; text-transform: uppercase; border-bottom: 2px solid var(--text-main); padding-bottom: 4px; color: var(--text-main); }
        h2 { font-size: 0.95rem; font-weight: 850; letter-spacing: 0.04em; text-transform: uppercase; border-bottom: 1px solid #e4e4e7; color: var(--text-muted); }
        .table-responsive { border: none; border-radius: 0; box-shadow: none; }
        table { border: none; border-bottom: 2px solid var(--text-main); }
        th { background: transparent; color: var(--text-main); border-bottom: 2px solid var(--text-main); padding-left: 0; }
        td { padding-left: 0; border-bottom: 1px solid #e4e4e7; }
        tbody tr:nth-child(even) td { background: transparent; }
        .callout { background: #ffffff; border: 1px solid #e4e4e7; border-left: 3.5px solid var(--accent); border-radius: 0; box-shadow: none; }
        .callout-objectifs::before { content: '■ OBJECTIFS'; font-size: 0.65rem; font-weight: 900; letter-spacing: 0.16em; text-transform: uppercase; color: var(--text-main); background: transparent; padding: 0; display: block; margin-bottom: 6px; }
        .callout-corrige::before, .callout-correction::before, .callout-solutions::before { content: '■ CORRIGÉ'; font-size: 0.65rem; font-weight: 900; letter-spacing: 0.16em; text-transform: uppercase; color: #059669; background: transparent; padding: 0; display: block; margin-bottom: 6px; }
        .callout-differentiation::before, .callout-aide::before, .callout-dys::before { content: '■ DIFFÉRENCIATION'; font-size: 0.65rem; font-weight: 900; letter-spacing: 0.16em; text-transform: uppercase; color: var(--text-main); background: transparent; padding: 0; display: block; margin-bottom: 6px; }
        .callout-warning::before, .callout-vigilance::before, .callout-attention::before { content: '■ POINT DE VIGILANCE'; font-size: 0.65rem; font-weight: 900; letter-spacing: 0.16em; text-transform: uppercase; color: #d97706; background: transparent; padding: 0; display: block; margin-bottom: 6px; }
        .key-value-grid .kv-item { background: #ffffff; border: 1px solid #e4e4e7; border-left: 3px solid var(--text-main); border-radius: 0; }
    ",
        StylePreset.HeaderCentered => @"
        /* Style classique / académique : prestige serif, typographie de manuel. */
        h1:not(.doc-title), h2, h3, th { font-family: Georgia, 'Palatino Linotype', Cambria, serif; }
        h1:not(.doc-title) { letter-spacing: 0.02em; font-weight: 700; font-variant: small-caps; border-bottom: 1px solid #cbd5e1; padding-bottom: 4px; }
        h2 { font-style: italic; font-weight: 600; color: #334155; border-bottom: none; }
        table { border: none; border-top: 2px solid var(--primary); border-bottom: 2px solid var(--primary); box-shadow: none; border-radius: 0; }
        th { background: transparent; color: var(--primary); font-variant: small-caps; border-bottom: 1px solid var(--primary); letter-spacing: 0.06em; }
        td { border-bottom: 1px solid #f1f5f9; }
        tbody tr:nth-child(even) td { background: transparent; }
        tbody td:first-child { font-style: italic; font-weight: 600; color: #1e293b; }
        .callout { background: #faf9f6; border-radius: 3px; border-left-width: 3.5px; border-color: #e7e2d9; }
        .callout-objectifs::before { content: '— OBJECTIFS DE LA SÉANCE —'; font-family: Georgia, serif; font-variant: small-caps; letter-spacing: 0.14em; font-weight: 700; color: var(--primary); background: transparent; padding: 0; display: block; margin-bottom: 6px; font-size: 0.72rem; }
        .callout-corrige::before, .callout-correction::before, .callout-solutions::before { content: '— CORRIGÉ & ANALYSE —'; font-family: Georgia, serif; font-variant: small-caps; letter-spacing: 0.14em; font-weight: 700; color: #881337; background: transparent; padding: 0; display: block; margin-bottom: 6px; font-size: 0.72rem; }
        .callout-differentiation::before, .callout-aide::before, .callout-dys::before { content: '— PARCOURS DIFFÉRENCIÉ —'; font-family: Georgia, serif; font-variant: small-caps; letter-spacing: 0.14em; font-weight: 700; color: #4338ca; background: transparent; padding: 0; display: block; margin-bottom: 6px; font-size: 0.72rem; }
        .callout-warning::before, .callout-vigilance::before, .callout-attention::before { content: '— VIGILANCE MÉTHODOLOGIQUE —'; font-family: Georgia, serif; font-variant: small-caps; letter-spacing: 0.14em; font-weight: 700; color: #92400e; background: transparent; padding: 0; display: block; margin-bottom: 6px; font-size: 0.72rem; }
        .key-value-grid .kv-item { background: #faf9f6; border-radius: 3px; border-top: 2.5px solid var(--primary); border-color: #e7e2d9; }
    ",
        StylePreset.HeaderBand => @"
        /* Style ludique : chaleureux, badges ronds. */
        h1:not(.doc-title) { font-size: 1.28rem; font-weight: 850; color: var(--primary); border-bottom: 2.5px dashed #c7d2fe; padding-bottom: 4px; }
        h2 { color: var(--primary); border-bottom: none; font-size: 1.05rem; font-weight: 750; }
        table { border: 1.5px solid #e0e7ff; border-radius: 12px; overflow: hidden; }
        th { background: linear-gradient(180deg, #ede9fe 0%, #e0e7ff 100%); color: #4338ca; border-bottom: 2px solid #a5b4fc; font-weight: 750; }
        tbody tr:nth-child(even) td { background: #fdf4ff; }
        tbody td:first-child { font-weight: 750; color: #4f46e5; }
        .callout { border-radius: 14px; border: 1.5px solid #e0e7ff; border-left: 4.5px solid #6366f1; }
        .callout-objectifs { background: #f5f3ff; border-color: #ddd6fe; border-left-color: #6366f1; }
        .callout-objectifs::before { content: '★ Objectifs de la séance'; font-size: 0.72rem; font-weight: 800; letter-spacing: 0.04em; color: #4f46e5; background: #ede9fe; padding: 2px 10px; border-radius: 999px; display: inline-block; margin-bottom: 8px; }
        .callout-corrige, .callout-correction, .callout-solutions { background: #f0fdf4; border-color: #bbf7d0; border-left-color: #22c55e; }
        .callout-corrige::before, .callout-correction::before, .callout-solutions::before { content: '✓ Corrigé & Solution'; font-size: 0.72rem; font-weight: 800; letter-spacing: 0.04em; color: #15803d; background: #dcfce7; padding: 2px 10px; border-radius: 999px; display: inline-block; margin-bottom: 8px; }
        .callout-differentiation, .callout-aide, .callout-dys { background: #fff1f2; border-color: #fecdd3; border-left-color: #f43f5e; }
        .callout-differentiation::before, .callout-aide::before, .callout-dys::before { content: '➜ Coup de pouce'; font-size: 0.72rem; font-weight: 800; letter-spacing: 0.04em; color: #be123c; background: #ffe4e6; padding: 2px 10px; border-radius: 999px; display: inline-block; margin-bottom: 8px; }
        .callout-warning, .callout-vigilance, .callout-attention { background: #fffbeb; border-color: #fde68a; border-left-color: #f59e0b; }
        .callout-warning::before, .callout-vigilance::before, .callout-attention::before { content: '! À retenir'; font-size: 0.72rem; font-weight: 800; letter-spacing: 0.04em; color: #b45309; background: #fef3c7; padding: 2px 10px; border-radius: 999px; display: inline-block; margin-bottom: 8px; }
        .key-value-grid .kv-item { border-radius: 12px; border: 1.5px solid #e0e7ff; border-top: 3.5px solid #f59e0b; }
    ",
        _ => @"
        /* Style moderne : hiérarchie éditoriale épurée. */
        h1:not(.doc-title) {
            font-size: 1.28rem;
            font-weight: 800;
            color: var(--primary);
            letter-spacing: -0.02em;
            border-bottom: 2px solid rgba(30, 58, 138, 0.12);
            padding-bottom: 5px;
            margin-top: 1.7em;
        }
        h2 { font-size: 1.05rem; font-weight: 700; color: var(--secondary); margin-top: 1.2em; border-bottom: none; }
        th { background: #f8fafc; color: #1e293b; border-bottom: 2px solid var(--primary); font-weight: 750; }
        tbody td:first-child { font-weight: 700; color: var(--primary); }
    "
    };

    /// <summary>Feuille de style du thème par défaut (« Moderne »).</summary>
    public static string DefaultCss => BuildPresetCss(null);
}
