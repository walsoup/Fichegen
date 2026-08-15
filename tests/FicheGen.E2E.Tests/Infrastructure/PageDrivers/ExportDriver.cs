// ============================================================================
//  FicheGen.E2E.Tests — ExportDriver
//  Pilote pour l'exportation PDF et Presse-papier (CF_HTML).
// ============================================================================

using System.IO;
using System.Threading.Tasks;
using FicheGen.App.Services;
using FicheGen.Core.Documents;
using FicheGen.Infrastructure.Export;

namespace FicheGen.E2E.Tests.Infrastructure.PageDrivers;

public sealed class ExportDriver
{
    private readonly TestEnvironment _env;

    public ExportDriver(TestEnvironment env)
    {
        _env = env;
    }

    public async Task<string> ExportToPdfAsync(GeneratedDocument doc, string outputPath)
    {
        var exporter = new WebView2PdfExporter();
        await exporter.ExportPdfToFileAsync(doc, outputPath);
        return outputPath;
    }

    public string BuildClipboardCfHtmlPayload(GeneratedDocument doc)
    {
        var html = HtmlRenderer.RenderToHtml(doc);
        return ClipboardPackageBuilder.FormatCfHtml(html);
    }
}
