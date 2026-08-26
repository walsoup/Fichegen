using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.App.ViewModels;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using FicheGen.Core.Storage;
using Serilog;

namespace FicheGen.App.Services;

public sealed class ExportWorkflowService : IExportWorkflowService
{
    private readonly PickerService _pickerService;
    private readonly IDocumentPdfExporter _pdfExporter;
    private readonly IDocxExporter _docxExporter;
    private readonly IRtfDocumentWriter _rtfWriter;
    private readonly ISettingsStore? _settingsStore;

    public ExportWorkflowService(
        PickerService pickerService,
        IDocumentPdfExporter pdfExporter,
        IDocxExporter docxExporter,
        IRtfDocumentWriter rtfWriter,
        ISettingsStore? settingsStore = null)
    {
        _pickerService = pickerService;
        _pdfExporter = pdfExporter;
        _docxExporter = docxExporter;
        _rtfWriter = rtfWriter;
        _settingsStore = settingsStore;
    }

    public async Task<string?> ExportPdfAsync(
        GeneratedDocument document,
        string html,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var targetPath = await ResolveDestinationPathAsync(
            suggestedFileName,
            ".pdf",
            "Document PDF (*.pdf)",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await _pdfExporter.ExportPdfToFileAsync(document, targetPath, null, cancellationToken);
        return targetPath;
    }

    public async Task<string?> ExportDocxAsync(
        GeneratedDocument document,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var targetPath = await ResolveDestinationPathAsync(
            suggestedFileName,
            ".docx",
            "Document Word (*.docx)",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await _docxExporter.ExportDocxToFileAsync(document, targetPath, null, cancellationToken);
        return targetPath;
    }

    public async Task<string?> ExportRtfAsync(
        GeneratedDocument document,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var targetPath = await ResolveDestinationPathAsync(
            suggestedFileName,
            ".rtf",
            "Document RTF (*.rtf)",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var bytes = _rtfWriter.ExportRtfBytes(document);
        await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken);
        return targetPath;
    }

    private async Task<string?> ResolveDestinationPathAsync(
        string suggestedFileName,
        string extension,
        string fileTypeDescription,
        CancellationToken cancellationToken)
    {
        // 1. Si un dossier d'exportation est configuré dans les Paramètres, on l'utilise directement
        try
        {
            var settings = _settingsStore?.GetSettings<AppSettings>();
            var exportsDir = settings?.Folders?.ExportsDir;
            if (!string.IsNullOrWhiteSpace(exportsDir))
            {
                if (!Directory.Exists(exportsDir))
                {
                    Directory.CreateDirectory(exportsDir);
                }

                return Path.Combine(exportsDir, suggestedFileName);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Lecture du dossier d'exportation des paramètres impossible.");
        }

        // 2. Sinon, on invite l'utilisateur à choisir l'emplacement via le sélecteur de fichier
        try
        {
            var choices = new Dictionary<string, IList<string>>
            {
                { fileTypeDescription, new List<string> { extension } }
            };

            var file = await _pickerService.PickSaveFileAsync(suggestedFileName, choices);
            if (file != null && !string.IsNullOrWhiteSpace(file.Path))
            {
                return file.Path;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Boîte de dialogue d'enregistrement indisponible, repli vers Documents.");
        }

        // 3. Repli automatique sécurisé vers Documents/PROFstudio/Exports si non configuré / annulé avec erreur
        try
        {
            var defaultFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PROFstudio",
                "Exports");
            Directory.CreateDirectory(defaultFolder);
            return Path.Combine(defaultFolder, suggestedFileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Impossible de préparer le dossier d'export par défaut.");
            return null;
        }
    }
}
