using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;

namespace FicheGen.Infrastructure.Export;

public sealed class DocxExporter : IDocxExporter
{
    public byte[] ExportDocx(GeneratedDocument doc, StylePreset? preset = null)
    {
        using var ms = new MemoryStream();
        CreateDocxStream(doc, ms, preset);
        return ms.ToArray();
    }

    public async Task ExportDocxToFileAsync(GeneratedDocument doc, string filePath, StylePreset? preset = null, CancellationToken ct = default)
    {
        var bytes = ExportDocx(doc, preset);
        await File.WriteAllBytesAsync(filePath, bytes, ct).ConfigureAwait(false);
    }

    private static void CreateDocxStream(GeneratedDocument doc, Stream outputStream, StylePreset? preset)
    {
        var activePreset = preset ?? StylePreset.Modern;
        using var wordDoc = WordprocessingDocument.Create(outputStream, WordprocessingDocumentType.Document);

        var mainPart = wordDoc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        // Page setup: A4 (11906 x 16838 dxa), margins from preset (mm -> dxa: 1 mm = 56.7 dxa)
        var marginDxa = (uint)Math.Round(activePreset.MarginMm * 56.7);
        var secProps = new SectionProperties(
            new PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = (int)marginDxa, Bottom = (int)marginDxa, Left = marginDxa, Right = marginDxa }
        );

        // Render Document Title & Subtitle
        if (!string.IsNullOrWhiteSpace(doc.Metadata.Title))
        {
            var titlePara = new Paragraph(
                new ParagraphProperties(
                    new ParagraphStyleId { Val = "Title" },
                    new SpacingBetweenLines { After = "120" }
                ),
                new Run(
                    new RunProperties(
                        new Bold(),
                        new FontSize { Val = "40" }, // 20pt
                        new Color { Val = HexColor(activePreset.PrimaryColor) }
                    ),
                    new Text(doc.Metadata.Title)
                )
            );
            body.AppendChild(titlePara);
        }

        if (!string.IsNullOrWhiteSpace(doc.Metadata.Subtitle))
        {
            var subPara = new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "240" }),
                new Run(
                    new RunProperties(
                        new Italic(),
                        new FontSize { Val = "24" }, // 12pt
                        new Color { Val = "64748B" }
                    ),
                    new Text(doc.Metadata.Subtitle)
                )
            );
            body.AppendChild(subPara);
        }

        // Render Document Blocks
        foreach (var block in doc.Blocks)
        {
            RenderBlock(block, body, activePreset);
        }

        body.AppendChild(secProps);
        mainPart.Document.Save();
    }

    private static void RenderBlock(Block block, Body body, StylePreset preset)
    {
        switch (block)
        {
            case HeadingBlock h:
                var fontSize = h.Level switch { 1 => "32", 2 => "28", _ => "24" };
                var para = new Paragraph(
                    new ParagraphProperties(
                        new SpacingBetweenLines { Before = "240", After = "120" }
                    )
                );
                foreach (var run in h.Runs)
                {
                    para.AppendChild(CreateRun(run, fontSize, bold: true, color: HexColor(preset.PrimaryColor)));
                }
                body.AppendChild(para);
                break;

            case ParagraphBlock p:
                var pPara = new Paragraph(
                    new ParagraphProperties(new SpacingBetweenLines { After = "160" })
                );
                foreach (var run in p.Runs)
                {
                    pPara.AppendChild(CreateRun(run, "22"));
                }
                body.AppendChild(pPara);
                break;

            case BulletListBlock bl:
                foreach (var item in bl.Items)
                {
                    var itemPara = new Paragraph(
                        new ParagraphProperties(
                            new SpacingBetweenLines { After = "80" },
                            new Indentation { Left = "360" }
                        ),
                        new Run(new RunProperties(new Bold()), new Text("• "))
                    );
                    foreach (var run in item)
                    {
                        itemPara.AppendChild(CreateRun(run, "22"));
                    }
                    body.AppendChild(itemPara);
                }
                break;

            case NumberedListBlock nl:
                for (int i = 0; i < nl.Items.Count; i++)
                {
                    var itemPara = new Paragraph(
                        new ParagraphProperties(
                            new SpacingBetweenLines { After = "80" },
                            new Indentation { Left = "360" }
                        ),
                        new Run(new RunProperties(new Bold()), new Text($"{i + 1}. "))
                    );
                    foreach (var run in nl.Items[i])
                    {
                        itemPara.AppendChild(CreateRun(run, "22"));
                    }
                    body.AppendChild(itemPara);
                }
                break;

            case TableBlock tbl:
                var openXmlTable = new Table();
                var tblProps = new TableProperties(
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 4, Color = "CBD5E1" },
                        new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "CBD5E1" },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                        new InsideVerticalBorder { Val = BorderValues.None }
                    ),
                    new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" } // 100%
                );
                openXmlTable.AppendChild(tblProps);

                if (tbl.Headers.Count > 0)
                {
                    var headerRow = new TableRow();
                    foreach (var h in tbl.Headers)
                    {
                        var cell = new TableCell(
                            new TableCellProperties(new Shading { Val = ShadingPatternValues.Clear, Fill = "F8FAFC" }),
                            new Paragraph(new Run(new RunProperties(new Bold()), new Text(h)))
                        );
                        headerRow.AppendChild(cell);
                    }
                    openXmlTable.AppendChild(headerRow);
                }

                foreach (var row in tbl.Rows)
                {
                    var tableRow = new TableRow();
                    foreach (var cellText in row)
                    {
                        var cell = new TableCell(new Paragraph(new Run(new Text(cellText))));
                        tableRow.AppendChild(cell);
                    }
                    openXmlTable.AppendChild(tableRow);
                }

                body.AppendChild(openXmlTable);
                body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "160" })));
                break;

            case KeyValueGridBlock kv:
                foreach (var pair in kv.Pairs)
                {
                    var kvPara = new Paragraph(
                        new ParagraphProperties(new SpacingBetweenLines { After = "80" }),
                        new Run(new RunProperties(new Bold(), new Color { Val = "64748B" }), new Text($"{pair.Key} : ") { Space = SpaceProcessingModeValues.Preserve }),
                        new Run(new Text(pair.Value) { Space = SpaceProcessingModeValues.Preserve })
                    );
                    body.AppendChild(kvPara);
                }
                break;

            case CalloutBoxBlock cb:
                foreach (var inner in cb.ContentBlocks)
                {
                    RenderBlock(inner, body, preset);
                }
                break;

            case PageBreakBlock:
                body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                break;
        }
    }

    private static Run CreateRun(TextRun run, string fontSizeHalfPoints, bool bold = false, string? color = null)
    {
        var runProps = new RunProperties();
        if (run.IsBold || bold) runProps.AppendChild(new Bold());
        if (run.IsItalic) runProps.AppendChild(new Italic());
        if (run.IsUnderline) runProps.AppendChild(new Underline { Val = UnderlineValues.Single });
        runProps.AppendChild(new FontSize { Val = fontSizeHalfPoints });
        if (!string.IsNullOrEmpty(color)) runProps.AppendChild(new Color { Val = color });

        return new Run(runProps, new Text(run.Text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static string HexColor(string colorHex)
    {
        if (string.IsNullOrEmpty(colorHex)) return "000000";
        return colorHex.TrimStart('#').ToUpperInvariant();
    }
}
