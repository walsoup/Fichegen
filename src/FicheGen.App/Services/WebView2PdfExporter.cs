using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using Microsoft.Web.WebView2.Core;

namespace FicheGen.App.Services;

public sealed class WebView2PdfExporter : IDocumentPdfExporter
{
    public async Task ExportPdfToFileAsync(GeneratedDocument doc, string outputPath, StylePreset? preset = null, CancellationToken ct = default)
    {
        var html = HtmlRenderer.RenderToHtml(doc, preset);
        var tempHtmlPath = Path.Combine(Path.GetTempPath(), $"fichegen_pdf_{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempHtmlPath, html, ct).ConfigureAwait(false);

        try
        {
            var webViewDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FicheGen",
                "WebView2");
            Directory.CreateDirectory(webViewDataFolder);

            var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, webViewDataFolder, null);
            // In headless/off-screen export mode, write HTML file and use CoreWebView2Environment if available, or write file directly
            if (File.Exists(tempHtmlPath))
            {
                // Fallback / vector file export placeholder
                await File.WriteAllTextAsync(outputPath, html, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (File.Exists(tempHtmlPath))
            {
                try { File.Delete(tempHtmlPath); } catch { }
            }
        }
    }
}
