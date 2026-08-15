using System.Text;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Toc;
using FicheGen.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests.Pdf;

public class PdfGuideServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly TocCacheStore _cacheStore;
    private readonly PdfGuideService _guideService;

    public PdfGuideServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"FicheGen_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _cacheStore = new TocCacheStore(_tempDirectory);
        _guideService = new PdfGuideService(_cacheStore);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
    }

    [Fact]
    public void TocCacheStore_SavesAndRetrieves_ValidCache()
    {
        var dummyPdfPath = Path.Combine(_tempDirectory, "dummy_guide.pdf");
        File.WriteAllText(dummyPdfPath, "%PDF-1.4 dummy pdf content");

        var dummyHash = TocCacheStore.ComputePdfHash(dummyPdfPath);
        var originalResult = new TocResult(
            dummyPdfPath,
            dummyHash,
            Offset: 12,
            OffsetConfidence: 0.9,
            LowConfidenceWarning: false,
            IsScanned: false,
            Entries: new List<ToCEntry>
            {
                new ToCEntry("Fractions", PrintedPage: 40, PhysicalPage: 52)
            }
        );

        // Save
        _cacheStore.SaveCache(originalResult);

        // Retrieve
        var retrieved = _cacheStore.TryGetCached(dummyPdfPath);

        retrieved.Should().NotBeNull();
        retrieved!.Offset.Should().Be(12);
        retrieved.Entries.Should().HaveCount(1);
        retrieved.Entries[0].Title.Should().Be("Fractions");
    }

    [Fact]
    public void TocCacheStore_CorruptCacheFile_SelfHealsByQuarantining()
    {
        var dummyPdfPath = Path.Combine(_tempDirectory, "dummy_guide.pdf");
        File.WriteAllText(dummyPdfPath, "%PDF-1.4 dummy pdf content");

        var cachePath = _cacheStore.GetCachePath(dummyPdfPath);
        File.WriteAllText(cachePath, "{ CORRUPT INVALID JSON }}}");

        var retrieved = _cacheStore.TryGetCached(dummyPdfPath);

        retrieved.Should().BeNull();
        File.Exists(cachePath).Should().BeFalse(); // Quarantined
        Directory.GetFiles(_tempDirectory, "*.corrupt-*").Should().NotBeEmpty();
    }

    [Fact]
    public void PdfGuideService_FindGuideFile_ResolvesMatchingLevelFile()
    {
        var guidesDir = Path.Combine(_tempDirectory, "Guides");
        Directory.CreateDirectory(guidesDir);

        var cm2File = Path.Combine(guidesDir, "guide_pedagogique_cm2.pdf");
        File.WriteAllText(cm2File, "content");

        var result = _guideService.FindGuideFile("CM2", guidesDir);

        result.Should().NotBeNull();
        result.Should().Be(cm2File);
    }

    [Fact]
    public async Task RealGuidePdf_GetToc_ExtractsEntries()
    {
        var realPdfPath = @"c:\Users\walid\work\goofy-goodall\GUIDE-ETINCELLE-MANUEL-MATHS-1AC.pdf";
        if (!File.Exists(realPdfPath)) return;

        var result = await _guideService.GetTocAsync(realPdfPath, CancellationToken.None);

        result.Should().NotBeNull();
        result.IsScanned.Should().BeFalse();
        result.Entries.Should().NotBeEmpty();
        result.Entries.Should().HaveCountGreaterThan(10);
    }
}
