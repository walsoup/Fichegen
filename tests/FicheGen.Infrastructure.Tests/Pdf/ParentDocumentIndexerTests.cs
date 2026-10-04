using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.Core.Toc;
using FicheGen.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests.Pdf;

public class ParentDocumentIndexerTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _cacheDirectory;
    private readonly TocCacheStore _cacheStore;
    private readonly ParentDocumentIndexer _indexer;

    public ParentDocumentIndexerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"FicheGen_IndexerTest_{Guid.NewGuid():N}");
        _cacheDirectory = Path.Combine(_tempDirectory, "cache");
        Directory.CreateDirectory(_tempDirectory);
        Directory.CreateDirectory(_cacheDirectory);

        _cacheStore = new TocCacheStore(_cacheDirectory);
        _indexer = new ParentDocumentIndexer(_cacheStore, llmClient: null, _cacheDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
    }

    [Fact]
    public async Task IndexAllAsync_EmptyDirectory_ReturnsEmptyIndex()
    {
        var emptyDir = Path.Combine(_tempDirectory, "EmptyGuides");
        Directory.CreateDirectory(emptyDir);

        var index = await _indexer.IndexAllAsync(emptyDir, ct: CancellationToken.None);

        index.Should().NotBeNull();
        index.Documents.Should().BeEmpty();
    }

    [Fact]
    public void SaveAndLoadIndex_RoundtripsSuccessfully()
    {
        var doc = new ParentDocumentItem(
            FilePath: @"C:\Guides\guide_cm1.pdf",
            FileName: "guide_cm1.pdf",
            DropdownLabel: "Guide-CM1-Maths",
            DocumentType: "Guide",
            Level: "CM1",
            Extra: "Maths",
            Lessons: new[] { new ToCEntry("Fractions", 10, 12) }
        );

        var index = new ParentDocumentsIndex(DateTime.UtcNow, @"C:\Guides", new[] { doc });

        _indexer.SaveIndex(index);
        var loaded = _indexer.LoadIndex();

        loaded.Should().NotBeNull();
        loaded!.Documents.Should().HaveCount(1);
        loaded.Documents[0].DropdownLabel.Should().Be("Guide-CM1-Maths");
        loaded.Documents[0].Lessons.Should().HaveCount(1);
        loaded.Documents[0].Lessons[0].Title.Should().Be("Fractions");
    }

    [Fact]
    public async Task IndexAllAsync_RealDocumentsGuides_ExtractsAndIndexesDocuments()
    {
        var realDir = @"C:\Users\walid\Documents\guides";
        if (!Directory.Exists(realDir)) return;

        var index = await _indexer.IndexAllAsync(
            realDir,
            confirmOfflineFallback: _ => Task.FromResult(true),
            ct: CancellationToken.None);

        index.Should().NotBeNull();
        index.Documents.Should().NotBeEmpty();
        index.Documents.Should().HaveCountGreaterThanOrEqualTo(5);

        var sixieme = index.Documents.FirstOrDefault(d => d.Level == "6e");
        sixieme.Should().NotBeNull();
        sixieme!.DropdownLabel.Should().Contain("6e");
        sixieme.Lessons.Should().NotBeEmpty();

        var cm1 = index.Documents.FirstOrDefault(d => d.Level == "CM1");
        cm1.Should().NotBeNull();
        cm1!.DropdownLabel.Should().Contain("CM1");
    }

    [Fact]
    public void DiscoverDocuments_RealDocumentsGuides_FindsAllPdfsAndDetectsLevels()
    {
        var realDir = @"C:\Users\walid\Documents\guides";
        if (!Directory.Exists(realDir)) return;

        var docs = _indexer.DiscoverDocuments(realDir);

        docs.Should().NotBeNull();
        docs.Should().HaveCountGreaterThanOrEqualTo(5);

        foreach (var doc in docs)
        {
            doc.FilePath.Should().EndWithEquivalentOf(".pdf");
            doc.Level.Should().NotBeNullOrWhiteSpace();
            doc.DropdownLabel.Should().NotBeNullOrWhiteSpace();
            doc.DocumentType.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task IndexSingleDocumentAndSaveAsync_RealPdf_IndexesAndSavesToDisk()
    {
        var realFile = @"C:\Users\walid\Documents\guides\GUIDE_ABC_en_SCIENCE 6E.pdf";
        if (!File.Exists(realFile)) return;

        var doc = await _indexer.IndexSingleDocumentAndSaveAsync(
            parentDocsDir: Path.GetDirectoryName(realFile)!,
            pdfPath: realFile,
            confirmOfflineFallback: _ => Task.FromResult(true),
            ct: CancellationToken.None);

        doc.Should().NotBeNull();
        doc!.Level.Should().Be("6e");
        doc.Lessons.Should().NotBeEmpty();

        var loaded = _indexer.LoadIndex();
        loaded.Should().NotBeNull();
        loaded!.Documents.Should().Contain(d => d.FilePath == realFile);
    }
}

