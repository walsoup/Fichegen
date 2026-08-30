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
    private readonly FicheGen.Core.Services.StylePresetService? _stylePresetService;

    public ExportWorkflowService(
        PickerService pickerService,
        IDocumentPdfExporter pdfExporter,
        IDocxExporter docxExporter,
        IRtfDocumentWriter rtfWriter,
        ISettingsStore? settingsStore = null,
        FicheGen.Core.Services.StylePresetService? stylePresetService = null)
    {
        _pickerService = pickerService;
        _pdfExporter = pdfExporter;
        _docxExporter = docxExporter;
        _rtfWriter = rtfWriter;
        _settingsStore = settingsStore;
        _stylePresetService = stylePresetService;
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
            L10n.Get("Result_DefaultPdfFilter", "Document PDF (*.pdf)"),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var preset = _stylePresetService?.GetPreset(document.Metadata.DocType == "dyslexie" ? "dyslexie" : "modern");
        await _pdfExporter.ExportPdfToFileAsync(document, targetPath, preset, cancellationToken);
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
            L10n.Get("Result_DefaultDocxFilter", "Document Word (*.docx)"),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var preset = _stylePresetService?.GetPreset(document.Metadata.DocType == "dyslexie" ? "dyslexie" : "modern");
        await _docxExporter.ExportDocxToFileAsync(document, targetPath, preset, cancellationToken);
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
            L10n.Get("Result_DefaultRtfFilter", "Document RTF (*.rtf)"),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var preset = _stylePresetService?.GetPreset(document.Metadata.DocType == "dyslexie" ? "dyslexie" : "modern");
        var bytes = _rtfWriter.ExportRtfBytes(document, preset);
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

                var baseTarget = Path.Combine(exportsDir, suggestedFileName);
                if (!File.Exists(baseTarget))
                {
                    return baseTarget;
                }

                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(suggestedFileName);
                var ext = Path.GetExtension(suggestedFileName);
                int counter = 1;
                string uniqueTarget;
                do
                {
                    uniqueTarget = Path.Combine(exportsDir, $"{fileNameWithoutExt} ({counter}){ext}");
                    counter++;
                } while (File.Exists(uniqueTarget));

                return uniqueTarget;
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
            // Si l'utilisateur a choisi un fichier, on retourne son chemin.
            // Si l'utilisateur a annulé le sélecteur (file == null), on retourne null immédiatement sans exporter de fichier.
            return file?.Path;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Boîte de dialogue d'enregistrement indisponible ou erreur d'accès.");
            return null;
        }
    }
}
