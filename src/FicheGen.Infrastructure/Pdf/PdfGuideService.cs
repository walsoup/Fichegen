using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Toc;
using Serilog;
using UglyToad.PdfPig;

namespace FicheGen.Infrastructure.Pdf;

public sealed class PdfGuideService : IPdfGuideService
{
    private readonly TocCacheStore _cacheStore;
    private readonly IOcrService? _ocrService;

    public PdfGuideService(TocCacheStore? cacheStore = null, IOcrService? ocrService = null)
    {
        _cacheStore = cacheStore ?? new TocCacheStore();
        _ocrService = ocrService;
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
                // Si l'OCR est disponible, tentons d'extraire la ToC des premières pages scannées
                if (_ocrService != null && _ocrService.IsSupported)
                {
                    try
                    {
                        var tocSb = new StringBuilder();
                        var ocrPages = Math.Min(10, pageCount);
                        for (var p = 1; p <= ocrPages; p++)
                        {
                            var pageOcr = _ocrService.ExtractTextFromPdfPageAsync(pdfPath, p, ct).GetAwaiter().GetResult();
                            if (!string.IsNullOrWhiteSpace(pageOcr))
                            {
                                tocSb.AppendLine(pageOcr);
                            }
                        }

                        var ocrToc = TocParser.ParseToc(tocSb.ToString());
                        if (ocrToc.Count > 0)
                        {
                            var ocrEntries = ocrToc.Select(t => new ToCEntry(t.Title, t.PrintedPage, t.PrintedPage)).ToList();
                            var ocrResult = new TocResult(
                                pdfPath,
                                pdfHash,
                                Offset: 0,
                                OffsetConfidence: 0.8,
                                LowConfidenceWarning: false,
                                IsScanned: true,
                                ocrEntries
                            );
                            _cacheStore.SaveCache(ocrResult);
                            return ocrResult;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Échec de l'extraction ToC par OCR pour le PDF scanné {File}.", Path.GetFileName(pdfPath));
                    }
                }

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

            // Extract ToC text from first 15 pages
            var tocPagesCount = Math.Min(15, pageCount);
            var sb = new StringBuilder();
            for (var p = 1; p <= tocPagesCount; p++)
            {
                sb.AppendLine(ExtractPageTextWithLines(document.GetPage(p)));
            }

            var parsedToc = TocParser.ParseToc(sb.ToString());

            // If no ToC found in front matter, check the back matter (last 15 pages)
            if (parsedToc.Count == 0 && pageCount > tocPagesCount)
            {
                var startBack = Math.Max(tocPagesCount + 1, pageCount - 15);
                var backSb = new StringBuilder();
                for (var p = startBack; p <= pageCount; p++)
                {
                    backSb.AppendLine(ExtractPageTextWithLines(document.GetPage(p)));
                }
                parsedToc = TocParser.ParseToc(backSb.ToString());
            }

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

        return Task.Run(async () =>
        {
            using var document = PdfDocument.Open(pdfPath);
            var totalPages = document.NumberOfPages;

            var start = Math.Clamp(physicalStartPage, 1, totalPages);
            var end = Math.Clamp(physicalEndPage, start, Math.Min(totalPages, start + 9)); // Cap at 10 pages max

            var sb = new StringBuilder();
            for (var p = start; p <= end; p++)
            {
                sb.AppendLine($"--- PAGE {p} ---");
                var page = document.GetPage(p);
                var pageText = page.Text?.Trim() ?? string.Empty;

                // Si la page est scannée ou sans couche texte suffisante (< 30 caractères) et que l'OCR est disponible
                if (pageText.Length < 30 && _ocrService != null && _ocrService.IsSupported)
                {
                    try
                    {
                        var ocrText = await _ocrService.ExtractTextFromPdfPageAsync(pdfPath, p, ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(ocrText))
                        {
                            pageText = ocrText;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "OCR fallback sur la page {Page} impossible.", p);
                    }
                }

                sb.AppendLine(pageText);
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

    private static string ExtractPageTextWithLines(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords();
        if (words == null || !words.Any())
            return page.Text ?? string.Empty;

        var lines = words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 4.0) * 4.0)
            .OrderByDescending(g => g.Key)
            .Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));

        return string.Join("\n", lines);
    }
}
