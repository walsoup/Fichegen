using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace FicheGen.App.Services;

public sealed class WebView2PdfExporter : IDocumentPdfExporter
{
    // WebView2 rejects concurrent CoreWebView2Environment instances sharing one
    // user-data folder — exports must be strictly sequential.
    private static readonly SemaphoreSlim ExportLock = new(1, 1);

    private readonly DispatcherQueue? _dispatcherQueue;

    public WebView2PdfExporter(DispatcherQueue? dispatcherQueue = null)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public async Task ExportPdfToFileAsync(GeneratedDocument doc, string outputPath, StylePreset? preset = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var html = HtmlRenderer.RenderToHtml(doc, preset);

        DispatcherQueue? queue = _dispatcherQueue;
        if (queue == null)
        {
            try { queue = App.CurrentMainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread(); } catch { queue = null; }
        }
        if (queue == null)
        {
            throw new InvalidOperationException("Un DispatcherQueue de thread UI est requis pour l'exportation PDF via WebView2.");
        }

        await ExportLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<bool>();
            using var reg = ct.Register(() => tcs.TrySetCanceled());

            var enqueued = queue.TryEnqueue(async () =>
            {
                Microsoft.UI.Xaml.Controls.WebView2? webView = null;
                var tempPdfPath = Path.Combine(Path.GetTempPath(), $"fichegen_{Guid.NewGuid():N}.pdf");

                try
                {
                    var webViewDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "FicheGen",
                        "WebView2");
                    Directory.CreateDirectory(webViewDataFolder);

                    var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, webViewDataFolder, null);

                    webView = new Microsoft.UI.Xaml.Controls.WebView2();
                    await webView.EnsureCoreWebView2Async(env);

                    var navTcs = new TaskCompletionSource<bool>();
                    void OnNavCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                    {
                        if (e.IsSuccess) navTcs.TrySetResult(true);
                        else navTcs.TrySetException(new InvalidOperationException($"Navigation error: {e.WebErrorStatus}"));
                    }

                    webView.CoreWebView2.NavigationCompleted += OnNavCompleted;
                    try
                    {
                        webView.NavigateToString(html);
                        await navTcs.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
                    }
                    finally
                    {
                        try { webView.CoreWebView2.NavigationCompleted -= OnNavCompleted; } catch { }
                    }

                    // Brief delay to ensure layout & fonts settle
                    await Task.Delay(100, ct);

                    var printSettings = env.CreatePrintSettings();
                    printSettings.Orientation = CoreWebView2PrintOrientation.Portrait;
                    printSettings.PageWidth = 8.27; // A4 width (inches)
                    printSettings.PageHeight = 11.69; // A4 height (inches)
                    printSettings.ShouldPrintBackgrounds = true;
                    printSettings.ShouldPrintHeaderAndFooter = false;
                    printSettings.ShouldPrintSelectionOnly = false;
                    if (preset != null)
                    {
                        var marginInches = preset.MarginMm / 25.4;
                        printSettings.MarginTop = marginInches;
                        printSettings.MarginBottom = marginInches;
                        printSettings.MarginLeft = marginInches;
                        printSettings.MarginRight = marginInches;
                    }

                    var printSuccess = await webView.CoreWebView2.PrintToPdfAsync(tempPdfPath, printSettings);
                    if (printSuccess && File.Exists(tempPdfPath))
                    {
                        var dir = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.Copy(tempPdfPath, outputPath, overwrite: true);
                        tcs.TrySetResult(true);
                    }
                    else
                    {
                        tcs.TrySetException(new InvalidOperationException("PrintToPdfAsync failed to generate PDF."));
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    if (File.Exists(tempPdfPath))
                    {
                        try { File.Delete(tempPdfPath); } catch { }
                    }
                    try { webView?.Close(); } catch { }
                }
            });

            if (!enqueued)
            {
                tcs.TrySetException(new InvalidOperationException("Impossible de planifier l'exportation PDF sur le thread UI."));
            }

            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            ExportLock.Release();
        }
    }
}
