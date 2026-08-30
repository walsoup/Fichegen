using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.Core.Abstractions;
using Serilog;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FicheGen.Infrastructure.Ocr;

/// <summary>
/// Implémentation native Windows (Windows.Media.Ocr) de la reconnaissance optique de caractères (OCR),
/// 100% hors-ligne, accélérée matériellement et sans dépendance externe.
/// </summary>
public sealed class WindowsNativeOcrService : IOcrService
{
    private OcrEngine? _ocrEngine;
    private bool _initialized;
    private readonly object _initLock = new();

    public bool IsSupported
    {
        get
        {
            EnsureEngine();
            return _ocrEngine != null;
        }
    }

    private void EnsureEngine()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;

        try
        {
            // Tente d'abord le français (cible principale des enseignants), puis les langues utilisateur, puis la première disponible
            var frenchLang = new Language("fr");
            if (OcrEngine.IsLanguageSupported(frenchLang))
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(frenchLang);
            }

            if (_ocrEngine == null)
            {
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            }

            if (_ocrEngine == null && OcrEngine.AvailableRecognizerLanguages.Count > 0)
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages.First());
            }

            if (_ocrEngine != null)
            {
                Log.Information("Moteur OCR Windows natif initialisé avec succès (Langue : {Language}).", _ocrEngine.RecognizerLanguage.DisplayName);
            }
            else
            {
                Log.Warning("Aucun pack de langue OCR disponible sur ce système Windows.");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Échec d'initialisation du moteur OCR Windows.");
            _ocrEngine = null;
        }
        finally
        {
            _initialized = true;
        }
        } // end lock
    }

    public async Task<string> ExtractTextFromPdfPageAsync(string pdfPath, int physicalPage, CancellationToken ct = default)
    {
        EnsureEngine();
        if (_ocrEngine == null || !File.Exists(pdfPath))
            return string.Empty;

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(pdfPath).AsTask(ct).ConfigureAwait(false);
            var pdfDoc = await PdfDocument.LoadFromFileAsync(storageFile).AsTask(ct).ConfigureAwait(false);

            try
            {
                var pageIndex = (uint)Math.Max(0, physicalPage - 1);
                if (pageIndex >= pdfDoc.PageCount)
                    return string.Empty;

                using var page = pdfDoc.GetPage(pageIndex);
                using var memoryStream = new InMemoryRandomAccessStream();

                // Rend à haute résolution pour une reconnaissance OCR optimale (1800px de large)
                var renderOptions = new PdfPageRenderOptions
                {
                    DestinationWidth = 1800
                };

                await page.RenderToStreamAsync(memoryStream, renderOptions).AsTask(ct).ConfigureAwait(false);
                memoryStream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(memoryStream).AsTask(ct).ConfigureAwait(false);
                using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied).AsTask(ct).ConfigureAwait(false);

                var result = await _ocrEngine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
                return CleanOcrText(result);
            }
            finally
            {
                ((global::WinRT.IWinRTObject)pdfDoc).NativeObject.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Erreur lors de l'OCR de la page {Page} du PDF {File}.", physicalPage, Path.GetFileName(pdfPath));
            return string.Empty;
        }
    }

    public async Task<string> ExtractTextFromImageFileAsync(string imagePath, CancellationToken ct = default)
    {
        EnsureEngine();
        if (_ocrEngine == null || !File.Exists(imagePath))
            return string.Empty;

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(imagePath).AsTask(ct).ConfigureAwait(false);
            using var fileStream = await storageFile.OpenAsync(FileAccessMode.Read).AsTask(ct).ConfigureAwait(false);

            var decoder = await BitmapDecoder.CreateAsync(fileStream).AsTask(ct).ConfigureAwait(false);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied).AsTask(ct).ConfigureAwait(false);

            var result = await _ocrEngine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
            return CleanOcrText(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Erreur lors de l'OCR de l'image {File}.", Path.GetFileName(imagePath));
            return string.Empty;
        }
    }

    public async Task<string> ExtractTextFromImageBytesAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        EnsureEngine();
        if (_ocrEngine == null || imageBytes == null || imageBytes.Length == 0)
            return string.Empty;

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(stream.GetOutputStreamAt(0));
            writer.WriteBytes(imageBytes);
            await writer.StoreAsync().AsTask(ct).ConfigureAwait(false);
            await writer.FlushAsync().AsTask(ct).ConfigureAwait(false);
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied).AsTask(ct).ConfigureAwait(false);

            var result = await _ocrEngine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
            return CleanOcrText(result);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Erreur lors de l'OCR du tableau d'octets image.");
            return string.Empty;
        }
    }

    private static string CleanOcrText(OcrResult result)
    {
        if (result == null || result.Lines == null || result.Lines.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var line in result.Lines)
        {
            sb.AppendLine(line.Text);
        }

        return sb.ToString().Trim();
    }
}
