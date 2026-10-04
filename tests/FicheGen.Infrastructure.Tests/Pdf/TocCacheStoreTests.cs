using FicheGen.Core.Abstractions;
using FicheGen.Core.Toc;
using FicheGen.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests.Pdf;

public class TocCacheStoreTests : IDisposable
{
    private readonly string _tempCacheDir;
    private readonly string _tempPdfFile;

    public TocCacheStoreTests()
    {
        _tempCacheDir = Path.Combine(Path.GetTempPath(), "FicheGenTests_Cache_" + Guid.NewGuid().ToString("N"));
        _tempPdfFile = Path.Combine(Path.GetTempPath(), "sample_" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(_tempPdfFile, new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }); // %PDF-1.4
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempCacheDir)) Directory.Delete(_tempCacheDir, true);
            if (File.Exists(_tempPdfFile)) File.Delete(_tempPdfFile);
        }
        catch { }
    }

    [Fact]
    public void ComputePdfHash_ConsistentAndNonEmptyForValidFile()
    {
        var hash1 = TocCacheStore.ComputePdfHash(_tempPdfFile);
        var hash2 = TocCacheStore.ComputePdfHash(_tempPdfFile);

        hash1.Should().NotBeNullOrWhiteSpace();
        hash1.Should().Be(hash2);

        var nonExistentHash = TocCacheStore.ComputePdfHash("C:\\does_not_exist_123.pdf");
        nonExistentHash.Should().BeEmpty();
    }

    [Fact]
    public void SaveAndLoad_RoundtripsToCEntriesAndOffset()
    {
        var store = new TocCacheStore(_tempCacheDir);
        var entries = new List<ToCEntry>
        {
            new("Introduction", 1, 5),
            new("Chapitre 1 - Les fractions", 10, 14),
            new("Chapitre 2 - Géométrie", 25, 29)
        };

        var result = new TocResult(
            PdfPath: _tempPdfFile,
            PdfHash: TocCacheStore.ComputePdfHash(_tempPdfFile),
            Offset: 4,
            OffsetConfidence: 0.95,
            LowConfidenceWarning: false,
            IsScanned: false,
            Entries: entries
        );

        store.SaveCache(result);

        var cached = store.TryGetCached(_tempPdfFile);

        cached.Should().NotBeNull();
        cached!.Offset.Should().Be(4);
        cached.OffsetConfidence.Should().Be(0.95);
        cached.LowConfidenceWarning.Should().BeFalse();
        cached.IsScanned.Should().BeFalse();
        cached.Entries.Should().HaveCount(3);
        cached.Entries[0].Title.Should().Be("Introduction");
        cached.Entries[0].PrintedPage.Should().Be(1);
        cached.Entries[0].PhysicalPage.Should().Be(5);
        cached.Entries[1].Title.Should().Be("Chapitre 1 - Les fractions");
        cached.Entries[1].PrintedPage.Should().Be(10);
        cached.Entries[1].PhysicalPage.Should().Be(14);
    }

    [Fact]
    public async Task TryGetCached_InvalidatesWhenFileModified()
    {
        var store = new TocCacheStore(_tempCacheDir);
        var entries = new List<ToCEntry> { new("Titre", 1, 1) };
        var result = new TocResult(
            PdfPath: _tempPdfFile,
            PdfHash: TocCacheStore.ComputePdfHash(_tempPdfFile),
            Offset: 0,
            OffsetConfidence: 1.0,
            LowConfidenceWarning: false,
            IsScanned: false,
            Entries: entries
        );

        store.SaveCache(result);

        var cachedBefore = store.TryGetCached(_tempPdfFile);
        cachedBefore.Should().NotBeNull();

        // Simulate file modification (re-write bytes and update timestamp)
        await Task.Delay(50);
        File.WriteAllBytes(_tempPdfFile, new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x35, 0x00 });

        var cachedAfter = store.TryGetCached(_tempPdfFile);
        cachedAfter.Should().BeNull("Cache hash must differ when file contents or write time changes.");
    }

    [Fact]
    public void SaveCache_ConcurrentSaves_DoNotThrowIOException()
    {
        var store = new TocCacheStore(_tempCacheDir);
        var entries = new List<ToCEntry> { new("Chapitre 1", 1, 5) };
        var result = new TocResult(
            PdfPath: _tempPdfFile,
            PdfHash: TocCacheStore.ComputePdfHash(_tempPdfFile),
            Offset: 4,
            OffsetConfidence: 0.95,
            LowConfidenceWarning: false,
            IsScanned: false,
            Entries: entries
        );

        // Concurrently save the exact same cache result across multiple threads
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        Parallel.For(0, 20, _ =>
        {
            try
            {
                store.SaveCache(result);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty("Concurrent saves must not throw IOException or collide on temporary files.");

        var cached = store.TryGetCached(_tempPdfFile);
        cached.Should().NotBeNull();
        cached!.Entries.Should().HaveCount(1);
    }

    [Fact]
    public void SaveCache_ConcurrentReadsAndWrites_DoNotThrowOrCorrupt()
    {
        var store = new TocCacheStore(_tempCacheDir);
        var entries = new List<ToCEntry> { new("Chapitre 1", 1, 5) };
        var result = new TocResult(
            PdfPath: _tempPdfFile,
            PdfHash: TocCacheStore.ComputePdfHash(_tempPdfFile),
            Offset: 4,
            OffsetConfidence: 0.95,
            LowConfidenceWarning: false,
            IsScanned: false,
            Entries: entries
        );

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        Parallel.For(0, 30, i =>
        {
            try
            {
                if (i % 2 == 0)
                {
                    store.SaveCache(result);
                }
                else
                {
                    _ = store.TryGetCached(_tempPdfFile);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty("Concurrent reads and writes must be safe and never throw.");
        var finalCached = store.TryGetCached(_tempPdfFile);
        finalCached.Should().NotBeNull();
    }

    [Fact]
    public void TryGetCached_CorruptJson_QuarantinesFile()
    {
        var store = new TocCacheStore(_tempCacheDir);
        var cachePath = store.GetCachePath(_tempPdfFile);
        File.WriteAllText(cachePath, "{ this is invalid json content !@#$");

        var result = store.TryGetCached(_tempPdfFile);

        result.Should().BeNull();
        File.Exists(cachePath).Should().BeFalse("Corrupt file should have been moved");
        var quarantinedFiles = Directory.EnumerateFiles(_tempCacheDir, "*.corrupt-*");
        quarantinedFiles.Should().NotBeEmpty("Corrupt file should have been quarantined");
    }
}
