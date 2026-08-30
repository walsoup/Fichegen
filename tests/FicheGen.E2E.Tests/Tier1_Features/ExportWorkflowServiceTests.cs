using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.App.Services;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using FicheGen.Core.Storage;
using FluentAssertions;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class ExportWorkflowServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ExportWorkflowServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fichegen_export_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private readonly AppSettings _settings;

        public FakeSettingsStore(AppSettings settings)
        {
            _settings = settings;
        }

        public T GetSettings<T>() where T : class, new()
        {
            return (_settings as T) ?? new T();
        }

        public Task SaveSettingsAsync<T>(T settings, CancellationToken ct = default) where T : class
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakePdfExporter : IDocumentPdfExporter
    {
        public Task ExportPdfToFileAsync(GeneratedDocument doc, string outputPath, StylePreset? preset = null, CancellationToken ct = default)
        {
            File.WriteAllText(outputPath, "%PDF-1.4 test content");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDocxExporter : IDocxExporter
    {
        public byte[] ExportDocx(GeneratedDocument doc, StylePreset? preset = null)
        {
            return System.Text.Encoding.UTF8.GetBytes("DOCX test content");
        }

        public Task ExportDocxToFileAsync(GeneratedDocument doc, string filePath, StylePreset? preset = null, CancellationToken ct = default)
        {
            File.WriteAllText(filePath, "DOCX test content");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRtfWriter : IRtfDocumentWriter
    {
        public string ExportRtfString(GeneratedDocument doc, StylePreset? preset = null)
        {
            return "{\\rtf1 test content}";
        }

        public byte[] ExportRtfBytes(GeneratedDocument doc, StylePreset? preset = null)
        {
            return System.Text.Encoding.UTF8.GetBytes("{\\rtf1 test content}");
        }
    }

    private static GeneratedDocument CreateSampleDocument()
    {
        var meta = new DocumentMetadata("Test Export", "Sous-titre", "CM1", "Français", 45, "#2B579A", "modern");
        var blocks = new Block[] { new HeadingBlock(1, "Test"), new ParagraphBlock("Contenu d'exportation.") };
        return new GeneratedDocument(meta, blocks);
    }

    [Fact]
    public async Task ExportPdfAsync_WithConfiguredExportsFolder_WritesDirectlyToFolder()
    {
        var customExportsDir = Path.Combine(_tempDir, "CustomExports");
        var settings = new AppSettings
        {
            Folders = new FolderSettings { ExportsDir = customExportsDir }
        };
        var settingsStore = new FakeSettingsStore(settings);

        var service = new ExportWorkflowService(
            new PickerService(),
            new FakePdfExporter(),
            new FakeDocxExporter(),
            new FakeRtfWriter(),
            settingsStore);

        var doc = CreateSampleDocument();
        var resultPath = await service.ExportPdfAsync(doc, "<html></html>", "test_fiche.pdf");

        resultPath.Should().NotBeNullOrWhiteSpace();
        resultPath!.Should().StartWith(customExportsDir);
        File.Exists(resultPath).Should().BeTrue();
    }

    [Fact]
    public async Task ExportDocxAsync_WithConfiguredExportsFolder_HandlesNameCollision()
    {
        var customExportsDir = Path.Combine(_tempDir, "Collisions");
        Directory.CreateDirectory(customExportsDir);
        var existingFile = Path.Combine(customExportsDir, "fiche.docx");
        await File.WriteAllTextAsync(existingFile, "Existing");

        var settings = new AppSettings
        {
            Folders = new FolderSettings { ExportsDir = customExportsDir }
        };
        var settingsStore = new FakeSettingsStore(settings);

        var service = new ExportWorkflowService(
            new PickerService(),
            new FakePdfExporter(),
            new FakeDocxExporter(),
            new FakeRtfWriter(),
            settingsStore);

        var doc = CreateSampleDocument();
        var resultPath = await service.ExportDocxAsync(doc, "fiche.docx");

        resultPath.Should().NotBeNullOrWhiteSpace();
        resultPath.Should().Be(Path.Combine(customExportsDir, "fiche (1).docx"));
        File.Exists(resultPath).Should().BeTrue();
    }

    [Fact]
    public async Task ExportRtfAsync_WithConfiguredExportsFolder_WritesRtfFile()
    {
        var customExportsDir = Path.Combine(_tempDir, "RtfExports");
        var settings = new AppSettings
        {
            Folders = new FolderSettings { ExportsDir = customExportsDir }
        };
        var settingsStore = new FakeSettingsStore(settings);

        var service = new ExportWorkflowService(
            new PickerService(),
            new FakePdfExporter(),
            new FakeDocxExporter(),
            new FakeRtfWriter(),
            settingsStore);

        var doc = CreateSampleDocument();
        var resultPath = await service.ExportRtfAsync(doc, "fiche.rtf");

        resultPath.Should().NotBeNullOrWhiteSpace();
        resultPath!.Should().StartWith(customExportsDir);
        File.Exists(resultPath).Should().BeTrue();
    }

    [Fact]
    public async Task ExportPdfAsync_WhenNoConfiguredExportsFolderAndPickerReturnsNull_ReturnsNullWithoutWritingFile()
    {
        var settings = new AppSettings
        {
            Folders = new FolderSettings { ExportsDir = string.Empty }
        };
        var settingsStore = new FakeSettingsStore(settings);

        var service = new ExportWorkflowService(
            new PickerService(),
            new FakePdfExporter(),
            new FakeDocxExporter(),
            new FakeRtfWriter(),
            settingsStore);

        var doc = CreateSampleDocument();
        // Picker without initialized HWND will return null / catch safely
        var resultPath = await service.ExportPdfAsync(doc, "<html></html>", "test_cancelled.pdf");

        resultPath.Should().BeNull();
    }
}
