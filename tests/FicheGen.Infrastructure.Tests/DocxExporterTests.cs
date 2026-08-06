using DocumentFormat.OpenXml.Packaging;
using FicheGen.Core.Documents;
using FicheGen.Infrastructure.Export;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class DocxExporterTests
{
    [Fact]
    public void ExportDocx_ValidDocument_ProducesValidOpenXmlPackage()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Fiche DOCX Test", Subtitle: "CM1 - Français"),
            Blocks: new List<Block>
            {
                new HeadingBlock(1, "Objectifs"),
                new ParagraphBlock("Ceci est un test d'exportation Word."),
                new TableBlock(
                    Headers: new List<string> { "Nom", "Note" },
                    Rows: new List<List<string>> { new() { "Élève A", "15/20" } }
                )
            }
        );

        var exporter = new DocxExporter();
        var bytes = exporter.ExportDocx(doc);

        bytes.Should().NotBeNullOrEmpty();

        using var ms = new MemoryStream(bytes);
        using var wordDoc = WordprocessingDocument.Open(ms, isEditable: false);

        wordDoc.MainDocumentPart.Should().NotBeNull();
        wordDoc.MainDocumentPart!.Document.Body.Should().NotBeNull();
        var bodyText = wordDoc.MainDocumentPart.Document.Body!.InnerText;

        bodyText.Should().Contain("Fiche DOCX Test");
        bodyText.Should().Contain("Objectifs");
        bodyText.Should().Contain("Élève A");
    }
}
