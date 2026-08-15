using FicheGen.App.ViewModels;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;

namespace FicheGen.App.Services;

public sealed class ExportWorkflowService : IExportWorkflowService
{
    private readonly PickerService _pickerService;
    private readonly IDocumentPdfExporter _pdfExporter;
    private readonly IDocxExporter _docxExporter;

    public ExportWorkflowService(
        PickerService pickerService,
        IDocumentPdfExporter pdfExporter,
        IDocxExporter docxExporter)
    {
        _pickerService = pickerService;
        _pdfExporter = pdfExporter;
        _docxExporter = docxExporter;
    }

    public async Task<string?> ExportPdfAsync(
        GeneratedDocument document,
        string html,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var choices = new Dictionary<string, IList<string>>
        {
            { "Document PDF (*.pdf)", new List<string> { ".pdf" } }
        };

        var file = await _pickerService.PickSaveFileAsync(suggestedFileName, choices);
        if (file is null) return null;

        await _pdfExporter.ExportPdfToFileAsync(document, file.Path, null, cancellationToken);
        return file.Path;
    }

    public async Task<string?> ExportDocxAsync(
        GeneratedDocument document,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var choices = new Dictionary<string, IList<string>>
        {
            { "Document Word (*.docx)", new List<string> { ".docx" } }
        };

        var file = await _pickerService.PickSaveFileAsync(suggestedFileName, choices);
        if (file is null) return null;

        await _docxExporter.ExportDocxToFileAsync(document, file.Path, null, cancellationToken);
        return file.Path;
    }

    public Task PrintAsync(string html, string documentTitle, CancellationToken cancellationToken = default)
    {
        // Handled directly by WebView2 PrintUI in PreviewHost
        return Task.CompletedTask;
    }
}
