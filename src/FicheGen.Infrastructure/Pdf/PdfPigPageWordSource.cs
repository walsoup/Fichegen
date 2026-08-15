using FicheGen.Core.Toc;
using UglyToad.PdfPig;

namespace FicheGen.Infrastructure.Pdf;

public sealed class PdfPigPageWordSource : IPageWordSource, IDisposable
{
    private readonly PdfDocument _document;
    private readonly bool _ownsDocument;

    public PdfPigPageWordSource(PdfDocument document, bool ownsDocument = false)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _ownsDocument = ownsDocument;
    }

    public int PageCount => _document.NumberOfPages;

    public IReadOnlyList<PageWord> GetWords(int physicalPage)
    {
        if (physicalPage < 1 || physicalPage > _document.NumberOfPages)
            return Array.Empty<PageWord>();

        var page = _document.GetPage(physicalPage);
        var words = page.GetWords();

        var result = new List<PageWord>();
        foreach (var word in words)
        {
            var bbox = word.BoundingBox;
            result.Add(new PageWord(
                word.Text,
                bbox.Bottom,
                bbox.Top,
                bbox.Height,
                page.Width,
                page.Height
            ));
        }

        return result;
    }

    public void Dispose()
    {
        if (_ownsDocument)
        {
            _document.Dispose();
        }
    }
}
