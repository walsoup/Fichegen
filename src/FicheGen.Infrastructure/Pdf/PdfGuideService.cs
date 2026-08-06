using System.Text;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Toc;
using UglyToad.PdfPig;

namespace FicheGen.Infrastructure.Pdf;

public sealed class PdfGuideService : IPdfGuideService
{
    private readonly TocCacheStore _cacheStore;

    public PdfGuideService(TocCacheStore? cacheStore = null)
    {
        _cacheStore = cacheStore ?? new TocCacheStore();
    }

    public string? FindGuideFile(string classLevel, string guidesDir)
    {
        if (string.IsNullOrWhiteSpace(guidesDir) || !Directory.Exists(guidesDir))
            return null;

        var normalizedLevel = classLevel.Trim().ToLowerInvariant();
        var files = Directory.GetFiles(guidesDir, "*.pdf", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (fileName.Contains(normalizedLevel) && (fileName.Contains("guide") || fileName.Contains("pedagogique")))
            {
                return file;
            }
        }

        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (fileName.Contains(normalizedLevel))
            {
                return file;
            }
        }

        return null;
    }

    public Task<TocResult> GetTocAsync(string pdfPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            throw new FileNotFoundException("Le fichier guide n'existe pas.", pdfPath);
        }

        return Task.Run(() =>
        {
            var cached = _cacheStore.TryGetCached(pdfPath);
            if (cached != null)
            {
                return cached;
            }

            var pdfHash = TocCacheStore.ComputePdfHash(pdfPath);
            using var document = PdfDocument.Open(pdfPath);

            var pageCount = document.NumberOfPages;

            // Check scanned status
            var totalLetters = 0;
            var samplePages = Math.Min(10, pageCount);
            for (var p = 1; p <= samplePages; p++)
            {
                totalLetters += document.GetPage(p).Letters.Count;
            }
            var isScanned = totalLetters == 0;

            if (isScanned)
            {
                var scannedResult = new TocResult(
                    pdfPath,
                    pdfHash,
                    Offset: 0,
                    OffsetConfidence: 0.0,
                    LowConfidenceWarning: true,
                    IsScanned: true,
                    Entries: Array.Empty<ToCEntry>()
                );
                _cacheStore.SaveCache(scannedResult);
                return scannedResult;
            }

            // Extract ToC text from first 12 pages
            var tocPagesCount = Math.Min(12, pageCount);
            var sb = new StringBuilder();
            for (var p = 1; p <= tocPagesCount; p++)
            {
                sb.AppendLine(document.GetPage(p).Text);
            }

            var parsedToc = TocParser.ParseToc(sb.ToString());

            // Detect page offset
            using var wordSource = new PdfPigPageWordSource(document);
            var offsetResult = PageOffsetDetector.DetectOffset(wordSource);

            var entries = new List<ToCEntry>();
            foreach (var (title, printedPage) in parsedToc)
            {
                var physicalPage = Math.Clamp(printedPage + offsetResult.Offset, 1, pageCount);
                entries.Add(new ToCEntry(title, printedPage, physicalPage));
            }

            var result = new TocResult(
                pdfPath,
                pdfHash,
                offsetResult.Offset,
                offsetResult.Confidence,
                offsetResult.LowConfidenceWarning,
                IsScanned: false,
                entries
            );

            _cacheStore.SaveCache(result);
            return result;
        }, ct);
    }

    public Task<string> ExtractLessonTextAsync(string pdfPath, int physicalStartPage, int physicalEndPage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            throw new FileNotFoundException("Le fichier guide n'existe pas.", pdfPath);
        }

        return Task.Run(() =>
        {
            using var document = PdfDocument.Open(pdfPath);
            var totalPages = document.NumberOfPages;

            var start = Math.Clamp(physicalStartPage, 1, totalPages);
            var end = Math.Clamp(physicalEndPage, start, Math.Min(totalPages, start + 9)); // Cap at 10 pages max

            var sb = new StringBuilder();
            for (var p = start; p <= end; p++)
            {
                sb.AppendLine($"--- PAGE {p} ---");
                sb.AppendLine(document.GetPage(p).Text);
            }

            var fullText = sb.ToString();

            // Truncate to ~30,000 characters preserving head and tail if over budget
            const int maxChars = 30000;
            if (fullText.Length > maxChars)
            {
                var head = fullText.Substring(0, 15000);
                var tail = fullText.Substring(fullText.Length - 15000);
                return $"{head}\n\n[... CONTENU TRONQUÉ POUR BUDGET PROMPT ...]\n\n{tail}";
            }

            return fullText;
        }, ct);
    }

    public Task<byte[]> GetPageThumbnailAsync(string pdfPath, int physicalPage, int width = 300, CancellationToken ct = default)
    {
        return WinRtPdfThumbnailRenderer.RenderPageThumbnailAsync(pdfPath, physicalPage, width, ct);
    }
}
