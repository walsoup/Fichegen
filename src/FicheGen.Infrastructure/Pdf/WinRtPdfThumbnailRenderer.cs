using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FicheGen.Infrastructure.Pdf;

public static class WinRtPdfThumbnailRenderer
{
    public static async Task<byte[]> RenderPageThumbnailAsync(string pdfPath, int physicalPage, int destinationWidth = 300, CancellationToken ct = default)
    {
        if (!File.Exists(pdfPath))
            return Array.Empty<byte>();

        var storageFile = await StorageFile.GetFileFromPathAsync(pdfPath).AsTask(ct).ConfigureAwait(false);
        var pdfDoc = await PdfDocument.LoadFromFileAsync(storageFile).AsTask(ct).ConfigureAwait(false);

        var pageIndex = (uint)Math.Max(0, physicalPage - 1);
        if (pageIndex >= pdfDoc.PageCount)
        {
            pageIndex = 0;
        }

        using var page = pdfDoc.GetPage(pageIndex);
        using var memoryStream = new InMemoryRandomAccessStream();

        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)destinationWidth
        };

        await page.RenderToStreamAsync(memoryStream, options).AsTask(ct).ConfigureAwait(false);

        var buffer = new byte[memoryStream.Size];
        using var dataReader = new DataReader(memoryStream.GetInputStreamAt(0));
        await dataReader.LoadAsync((uint)memoryStream.Size).AsTask(ct).ConfigureAwait(false);
        dataReader.ReadBytes(buffer);

        return buffer;
    }
}
