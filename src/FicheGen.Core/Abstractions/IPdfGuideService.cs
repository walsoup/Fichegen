using FicheGen.Core.Toc;

namespace FicheGen.Core.Abstractions;

public sealed record TocResult(
    string PdfPath,
    string PdfHash,
    int Offset,
    double OffsetConfidence,
    bool LowConfidenceWarning,
    bool IsScanned,
    IReadOnlyList<ToCEntry> Entries);

public interface IPdfGuideService
{
    string? FindGuideFile(string classLevel, string guidesDir);

    Task<TocResult> GetTocAsync(string pdfPath, CancellationToken ct);

    Task<string> ExtractLessonTextAsync(string pdfPath, int physicalStartPage, int physicalEndPage, CancellationToken ct);

    Task<byte[]> GetPageThumbnailAsync(string pdfPath, int physicalPage, int width = 300, CancellationToken ct = default);
}
