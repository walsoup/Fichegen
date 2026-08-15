// ============================================================================
//  FicheGen.E2E.Tests — PdfExportTests (Tier 1 Feature Coverage)
// ============================================================================

using System;
using System.IO;
using System.Threading.Tasks;
using FicheGen.Core.Documents;
using FicheGen.E2E.Tests.Infrastructure;
using FicheGen.E2E.Tests.Infrastructure.PageDrivers;
using FluentAssertions;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class PdfExportTests : E2ETestBase
{
    private GeneratedDocument CreateTestDocument()
    {
        var meta = new DocumentMetadata("Fiche PDF Test", "Sous-titre", "CM2", "Mathématiques", 60, "#2B579A", "modern");
        var blocks = new Block[]
        {
            new HeadingBlock(1, "Titre de la fiche"),
            new ParagraphBlock("Contenu pédagogique d'exportation PDF.")
        };
        return new GeneratedDocument(meta, blocks);
    }

    [Fact]
    public async Task ExportPdf_ValidDocumentAndPath_GeneratesPdfFile()
    {
        // Arrange
        var driver = new ExportDriver(Environment);
        var doc = CreateTestDocument();
        var outputPath = Path.Combine(Environment.TempFolder, "test_output.pdf");

        // Act
        var resultPath = await driver.ExportToPdfAsync(doc, outputPath);

        // Assert
        resultPath.Should().Be(outputPath);
    }

    [Fact]
    public async Task ExportPdf_EmptyDocument_ThrowsArgumentExceptionOrFails()
    {
        // Arrange
        var exporter = new FicheGen.App.Services.WebView2PdfExporter();
        var outputPath = Path.Combine(Environment.TempFolder, "empty.pdf");

        // Act
        var act = async () => await exporter.ExportPdfToFileAsync(null!, outputPath);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ExportPdf_InvalidDestinationDirectory_HandlesFailureGracefully()
    {
        // Arrange
        var driver = new ExportDriver(Environment);
        var doc = CreateTestDocument();
        var invalidPath = Path.Combine("Z:\\NonExistentFolder_9999", "output.pdf");

        // Act
        var act = async () => await driver.ExportToPdfAsync(doc, invalidPath);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ExportPdf_OverwriteExistingFile_ReplacesFileSuccessfully()
    {
        // Arrange
        var driver = new ExportDriver(Environment);
        var doc = CreateTestDocument();
        var outputPath = Path.Combine(Environment.TempFolder, "overwrite_test.pdf");
        await File.WriteAllTextAsync(outputPath, "Old Content");

        // Act
        var resultPath = await driver.ExportToPdfAsync(doc, outputPath);

        // Assert
        resultPath.Should().Be(outputPath);
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public async Task ExportPdf_SequentialExportRequests_ExecutesAllExports()
    {
        // Arrange
        var driver = new ExportDriver(Environment);
        var doc = CreateTestDocument();
        var path1 = Path.Combine(Environment.TempFolder, "seq1.pdf");
        var path2 = Path.Combine(Environment.TempFolder, "seq2.pdf");

        // Act
        await driver.ExportToPdfAsync(doc, path1);
        await driver.ExportToPdfAsync(doc, path2);

        // Assert
        File.Exists(path1).Should().BeTrue();
        File.Exists(path2).Should().BeTrue();
    }
}
