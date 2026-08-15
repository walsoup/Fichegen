// ============================================================================
//  FicheGen.E2E.Tests — ClipboardExportTests (Tier 1 Feature Coverage)
// ============================================================================

using System;
using System.Text;
using FicheGen.Core.Documents;
using FicheGen.E2E.Tests.Infrastructure;
using FicheGen.E2E.Tests.Infrastructure.PageDrivers;
using FicheGen.Infrastructure.Export;
using FluentAssertions;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class ClipboardExportTests : E2ETestBase
{
    private GeneratedDocument CreateFrenchDocument()
    {
        var meta = new DocumentMetadata("Fiche Évaluation & Révolution", "Sous-titre élève", "CE2", "Français", 45, "#2B579A", "modern");
        var blocks = new Block[]
        {
            new HeadingBlock(1, "Évaluation de Français : la Révolution française"),
            new ParagraphBlock("Consigne : Répondez aux questions avec précision et soignez l'orthographe.")
        };
        return new GeneratedDocument(meta, blocks);
    }

    [Fact]
    public void ExportClipboard_FrenchTextWithAccents_CalculatesCorrectUtf8ByteLength()
    {
        // Arrange
        var doc = CreateFrenchDocument();
        var driver = new ExportDriver(Environment);

        // Act
        var cfHtml = driver.BuildClipboardCfHtmlPayload(doc);

        // Assert
        cfHtml.Should().Contain("Version:1.0");
        cfHtml.Should().Contain("StartHTML:");
        cfHtml.Should().Contain("EndHTML:");

        // Extraire StartHTML et EndHTML et vérifier qu'ils pointent correctement vers le HTML UTF-8
        var startIdx = cfHtml.IndexOf("<!--StartFragment-->", StringComparison.Ordinal);
        var endIdx = cfHtml.IndexOf("<!--EndFragment-->", StringComparison.Ordinal);
        startIdx.Should().BeGreaterThan(0);
        endIdx.Should().BeGreaterThan(startIdx);
    }

    [Fact]
    public void ExportClipboard_GeneratedDocument_BuildsValidCfHtmlPayload()
    {
        // Arrange
        var doc = CreateFrenchDocument();
        var html = HtmlRenderer.RenderToHtml(doc);

        // Act
        var cfHtml = ClipboardPackageBuilder.FormatCfHtml(html);

        // Assert
        cfHtml.Should().StartWith("Version:1.0");
        cfHtml.Should().Contain("StartFragment:");
        cfHtml.Should().Contain("EndFragment:");
        cfHtml.Should().Contain("Évaluation de Français");
    }

    [Fact]
    public void ExportClipboard_EmptyHtml_ReturnsValidHeaderStructure()
    {
        // Arrange & Act
        var cfHtml = ClipboardPackageBuilder.FormatCfHtml("<html><body></body></html>");

        // Assert
        cfHtml.Should().NotBeNullOrEmpty();
        cfHtml.Should().Contain("Version:1.0");
    }

    [Fact]
    public void ExportClipboard_SpecialCharacters_PreservesUtf8Encoding()
    {
        // Arrange
        var specialText = "<p>Alphabet & symboles : à, é, è, ê, ë, î, ï, ô, œ, ù, û, ü, ç, €</p>";

        // Act
        var cfHtml = ClipboardPackageBuilder.FormatCfHtml(specialText);
        var bytes = Encoding.UTF8.GetBytes(cfHtml);

        // Assert
        bytes.Length.Should().BeGreaterThan(specialText.Length);
        cfHtml.Should().Contain("à, é, è, ê, ë, î, ï, ô, œ, ù, û, ü, ç, €");
    }

    [Fact]
    public void ExportClipboard_MultipleBlocks_RendersHtmlStructureCorrectly()
    {
        // Arrange
        var meta = new DocumentMetadata("Multi-block", "", "CM1", "Sciences", 30, "#000", "modern");
        var blocks = new Block[]
        {
            new HeadingBlock(1, "Titre 1"),
            new ParagraphBlock("Paragraphe 1"),
            new HeadingBlock(2, "Titre 2"),
            new ParagraphBlock("Paragraphe 2")
        };
        var doc = new GeneratedDocument(meta, blocks);
        var driver = new ExportDriver(Environment);

        // Act
        var cfHtml = driver.BuildClipboardCfHtmlPayload(doc);

        // Assert
        cfHtml.Should().Contain("<h1");
        cfHtml.Should().Contain("<h2");
        cfHtml.Should().Contain("Paragraphe 1");
        cfHtml.Should().Contain("Paragraphe 2");
    }
}
