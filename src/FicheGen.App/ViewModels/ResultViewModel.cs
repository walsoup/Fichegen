// ============================================================================
//  FicheGen — ResultViewModel
//  Aperçu du document généré : thèmes, zoom, export, impression, recherche,
//  annuler/rétablir (profondeur 10), diagnostics.
//  Propriétés partielles (WinRT AOT) · Persistance zoom/préréglage
// ============================================================================

using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.App.Services;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using FicheGen.Core.Services;

namespace FicheGen.App.ViewModels;

/// <summary>Type d'export demandé pour le document courant.</summary>
public enum ExportKind
{
    Pdf,
    Docx,
    Rtf
}

/// <summary>Arguments de l'événement <see cref="ResultViewModel.ExportRequested"/>.</summary>
public sealed class ExportRequestedEventArgs : EventArgs
{
    public ExportRequestedEventArgs(ExportKind kind, GeneratedDocument document, string html, string suggestedFileName)
    {
        Kind = kind;
        Document = document;
        Html = html;
        SuggestedFileName = suggestedFileName;
    }

    public ExportKind Kind { get; }
    public GeneratedDocument Document { get; }
    public string Html { get; }
    public string SuggestedFileName { get; }
}

/// <summary>Arguments de l'événement <see cref="ResultViewModel.PrintRequested"/>.</summary>
public sealed class PrintRequestedEventArgs : EventArgs
{
    public PrintRequestedEventArgs(string html, string title)
    {
        Html = html;
        Title = title;
    }

    public string Html { get; }
    public string Title { get; }
}

/// <summary>Arguments de l'événement <see cref="ResultViewModel.CopyRequested"/>.</summary>
public sealed class CopyRequestedEventArgs : EventArgs
{
    public CopyRequestedEventArgs(string text) => Text = text;
    public string Text { get; }
}

/// <summary>Aide-mémoire d'un raccourci clavier (panneau d'aide de la vue).</summary>
public sealed record ShortcutHint(string Gesture, string Description);

/// <summary>Thème d'aperçu disponible dans le sélecteur de style.</summary>
public sealed record PresetOption(string Id, string Label, string Description);

/// <summary>Persiste les préférences d'aperçu (zoom, thème actif).</summary>
public interface IPreviewPreferencesStore
{
    double? LoadZoomFactor();
    void SaveZoomFactor(double zoomFactor);
    string? LoadActivePresetId();
    void SaveActivePresetId(string presetId);
}

/// <summary>
/// Flux complet d'export/impression (sélecteur de fichier + écriture).
/// Implémenté dans la couche App/Infrastructure ; en son absence, le
/// vue-modèle lève des événements que la vue peut traiter.
/// </summary>
public interface IExportWorkflowService
{
    Task<string?> ExportPdfAsync(GeneratedDocument document, string html, string suggestedFileName, CancellationToken cancellationToken = default);
    Task<string?> ExportDocxAsync(GeneratedDocument document, string suggestedFileName, CancellationToken cancellationToken = default);
    Task PrintAsync(string html, string documentTitle, CancellationToken cancellationToken = default);
}

/// <summary>Produit une archive ZIP de diagnostic et retourne son chemin.</summary>
public interface IDiagnosticZipExporter
{
    Task<string> ExportAsync(IReadOnlyDictionary<string, string?> contextEntries, CancellationToken cancellationToken = default);
}

/// <summary>
/// Vue-modèle de l'aperçu de résultat.
/// Clavier : Ctrl+Maj+E (PDF) · Ctrl+Maj+W (Word) · Ctrl+P (imprimer) ·
/// Ctrl+Z / Ctrl+Y (annuler/rétablir) · Ctrl+F (rechercher) · Échap (fermer).
/// </summary>
public partial class ResultViewModel : ObservableObject
{
    public const double MinZoom = 0.5;
    public const double MaxZoom = 2.0;
    public const double ZoomStep = 0.1;

    private const int MaxHistoryDepth = 10;

    private readonly IDocumentPdfExporter _pdfExporter;
    private readonly IDocxExporter _docxExporter;
    private readonly IRtfDocumentWriter _rtfWriter;
    private readonly StylePresetService _stylePresetService;
    private readonly IPreviewPreferencesStore? _preferencesStore;
    private readonly IExportWorkflowService? _exportWorkflow;
    private readonly IDiagnosticZipExporter? _diagnosticZipExporter;

    // Piles annuler / rétablir (profondeur bornée à 10 instantanés).
    private readonly List<DocumentSnapshot> _undoStack = new();
    private readonly List<DocumentSnapshot> _redoStack = new();
    private string _plainTextCache = string.Empty;

    // ------------------------------------------------------------------
    // Document & rendu
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    [NotifyPropertyChangedFor(nameof(DocumentStatsText))]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    [NotifyCanExecuteChangedFor(nameof(ExportPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportDocxCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPlainTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFindBarCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseDocumentCommand))]
    public partial GeneratedDocument? CurrentDocument { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewHtml))]
    public partial string CurrentHtml { get; set; }

    public string PreviewHtml => CurrentHtml;

    [ObservableProperty]
    public partial string ActivePresetId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomPercentText))]
    [NotifyCanExecuteChangedFor(nameof(ZoomInCommand))]
    [NotifyCanExecuteChangedFor(nameof(ZoomOutCommand))]
    public partial double ZoomFactor { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial StatusSeverity CurrentStatusSeverity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StudentViewButtonLabel))]
    [NotifyPropertyChangedFor(nameof(StudentViewButtonIcon))]
    public partial bool IsStudentView { get; set; }

    public string StudentViewButtonLabel => IsStudentView ? "Version Élève" : "Corrigé Enseignant";
    public string StudentViewButtonIcon => IsStudentView ? "\uE77B" : "\uE7BE";

    [ObservableProperty]
    public partial string? LastExportedFilePath { get; set; }

    [ObservableProperty]
    public partial bool ShowExportSuccessBanner { get; set; }

    [ObservableProperty]
    public partial string ExportSuccessMessage { get; set; }

    // ------------------------------------------------------------------
    // Annuler / Rétablir
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool CanUndo { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    public partial bool CanRedo { get; set; }

    [ObservableProperty]
    public partial string UndoDescription { get; set; }

    [ObservableProperty]
    public partial string RedoDescription { get; set; }

    // ------------------------------------------------------------------
    // Recherche dans le document (Ctrl+F)
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindNextCommand))]
    public partial bool IsFindBarVisible { get; set; }

    [ObservableProperty]
    public partial string FindQuery { get; set; }

    [ObservableProperty]
    public partial int FindMatchCount { get; set; }

    [ObservableProperty]
    public partial string FindStatusText { get; set; }

    // ------------------------------------------------------------------
    // Statistiques du document
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentStatsText))]
    public partial int WordCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentStatsText))]
    public partial int BlockCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentStatsText))]
    public partial int EstimatedReadingMinutes { get; set; }

    // ------------------------------------------------------------------
    // Propriétés calculées
    // ------------------------------------------------------------------

    public bool HasDocument => CurrentDocument is not null;

    public string DocumentTitle => CurrentDocument?.Metadata.Title ?? string.Empty;

    public string ZoomPercentText => $"{ZoomFactor:P0}";

    public string DocumentStatsText => HasDocument
        ? $"{WordCount} mots · {BlockCount} sections · ≈ {EstimatedReadingMinutes} min de lecture"
        : string.Empty;

    // ------------------------------------------------------------------
    // Événements (traités par la vue : sélecteurs de fichiers, impression…)
    // ------------------------------------------------------------------

    /// <summary>Déclenché quand le thème d'aperçu change (notification de l'interface).</summary>
    public event EventHandler<string>? PresetChanged;

    /// <summary>Déclenché si aucun <see cref="IExportWorkflowService"/> n'est injecté.</summary>
    public event EventHandler<ExportRequestedEventArgs>? ExportRequested;

    /// <summary>Déclenché si aucun <see cref="IExportWorkflowService"/> n'est injecté.</summary>
    public event EventHandler<PrintRequestedEventArgs>? PrintRequested;

    /// <summary>Déclenché pour que la vue place le texte dans le presse-papiers.</summary>
    public event EventHandler<CopyRequestedEventArgs>? CopyRequested;

    /// <summary>Déclenché pour créer une évaluation suite au document courant (Sprint 6 UX Document Chaining).</summary>
    public event EventHandler<GeneratedDocument>? CreateEvaluationRequested;

    /// <summary>Déclenché pour créer un quiz suite au document courant.</summary>
    public event EventHandler<GeneratedDocument>? CreateQuizRequested;

    [RelayCommand]
    public void CreateFollowUpEvaluation()
    {
        if (CurrentDocument != null)
        {
            CreateEvaluationRequested?.Invoke(this, CurrentDocument);
        }
    }

    [RelayCommand]
    public void CreateFollowUpQuiz()
    {
        if (CurrentDocument != null)
        {
            CreateQuizRequested?.Invoke(this, CurrentDocument);
        }
    }

    // ------------------------------------------------------------------
    // Catalogues exposés à la vue
    // ------------------------------------------------------------------

    public IReadOnlyList<PresetOption> AvailablePresets { get; } = new[]
    {
        new PresetOption("modern", "Moderne", "Épuré, aéré et contemporain."),
        new PresetOption("classique", "Classique", "Présentation académique traditionnelle."),
        new PresetOption("dys", "Lisibilité renforcée", "Interlignes et polices adaptés aux élèves DYS."),
        new PresetOption("compact", "Compact", "Densité optimisée pour l'impression.")
    };

    public IReadOnlyList<double> ZoomPresets { get; } = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

    public IReadOnlyList<ShortcutHint> ShortcutHints { get; } = new[]
    {
        new ShortcutHint("Ctrl+Maj+E", "Exporter le document en PDF"),
        new ShortcutHint("Ctrl+Maj+W", "Exporter le document en Word (.docx)"),
        new ShortcutHint("Ctrl+P", "Imprimer le document"),
        new ShortcutHint("Ctrl+Z", "Annuler la dernière modification"),
        new ShortcutHint("Ctrl+Y", "Rétablir la modification annulée"),
        new ShortcutHint("Ctrl+F", "Rechercher dans le document"),
        new ShortcutHint("Échap", "Fermer la recherche / annuler l'opération"),
        new ShortcutHint("Ctrl + Molette", "Zoomer dans l'aperçu")
    };

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    public ResultViewModel(
        IDocumentPdfExporter? pdfExporter = null,
        IDocxExporter? docxExporter = null,
        IRtfDocumentWriter? rtfWriter = null,
        StylePresetService? stylePresetService = null,
        IPreviewPreferencesStore? previewPreferencesStore = null,
        IExportWorkflowService? exportWorkflow = null,
        IDiagnosticZipExporter? diagnosticZipExporter = null)
    {
        _pdfExporter = pdfExporter!;
        _docxExporter = docxExporter!;
        _rtfWriter = rtfWriter!;
        _stylePresetService = stylePresetService ?? new StylePresetService();
        _preferencesStore = previewPreferencesStore;
        _exportWorkflow = exportWorkflow;
        _diagnosticZipExporter = diagnosticZipExporter;

        CurrentHtml = string.Empty;
        ActivePresetId = "modern";
        ZoomFactor = 1.0;
        StatusMessage = "Aucun document — générez une fiche, une évaluation ou un quiz.";
        CurrentStatusSeverity = StatusSeverity.Info;
        UndoDescription = string.Empty;
        RedoDescription = string.Empty;
        FindQuery = string.Empty;
        FindStatusText = string.Empty;
        ExportSuccessMessage = string.Empty;

        // Restauration des préférences d'aperçu persistées (zoom + thème).
        try
        {
            var persistedZoom = _preferencesStore?.LoadZoomFactor();
            if (persistedZoom is >= MinZoom and <= MaxZoom) ZoomFactor = persistedZoom.Value;

            var persistedPreset = _preferencesStore?.LoadActivePresetId();
            if (!string.IsNullOrWhiteSpace(persistedPreset)) ActivePresetId = persistedPreset;
        }
        catch
        {
            // Préférences illisibles : les valeurs par défaut s'appliquent.
        }
    }

    // ------------------------------------------------------------------
    // Chargement de document
    // ------------------------------------------------------------------

    /// <summary>Charge un document généré, réinitialise l'historique et calcule les statistiques.</summary>
    public void LoadDocument(GeneratedDocument document, string html, string? presetId = null)
    {
        CurrentDocument = document;
        IsStudentView = false;
        ShowExportSuccessBanner = false;
        LastExportedFilePath = null;
        CurrentHtml = html;
        if (!string.IsNullOrEmpty(presetId)) ActivePresetId = presetId;

        // Un nouveau document invalide l'historique de modifications.
        _undoStack.Clear();
        _redoStack.Clear();
        RefreshHistoryState();

        IsFindBarVisible = false;
        FindQuery = string.Empty;

        ComputeDocumentStats();
        SetStatus($"Document prêt — {DocumentStatsText}.", StatusSeverity.Success);
    }

    /// <summary>Bascule entre le mode Corrigé enseignant et la Version élève (avec lignes pointillées).</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    public void ToggleStudentView()
    {
        IsStudentView = !IsStudentView;
        RefreshRendering();
        SetStatus(
            IsStudentView ? "Affichage : Version Élève (prête pour impression/distribution)." : "Affichage : Version Enseignant (avec corrigé complet).",
            StatusSeverity.Info);
    }

    /// <summary>Re-génère le HTML d'aperçu avec le thème actif (après modification IA ou bascule élève/corrigé).</summary>
    public void RefreshRendering()
    {
        if (CurrentDocument is null) return;
        try
        {
            CurrentHtml = HtmlRenderer.RenderToHtml(CurrentDocument, _stylePresetService.GetPreset(ActivePresetId), isStudentVersion: IsStudentView);
        }
        catch
        {
            CurrentHtml = HtmlRenderer.RenderToHtml(CurrentDocument, isStudentVersion: IsStudentView);
        }
        ComputeDocumentStats();
    }

    /// <summary>Ferme le document courant et revient à l'état vide.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    public void CloseDocument()
    {
        CurrentDocument = null;
        CurrentHtml = string.Empty;
        _plainTextCache = string.Empty;
        _undoStack.Clear();
        _redoStack.Clear();
        RefreshHistoryState();
        ComputeDocumentStats();
        SetStatus("Document fermé.", StatusSeverity.Info);
    }

    // ------------------------------------------------------------------
    // Thèmes d'aperçu (notification + persistance)
    // ------------------------------------------------------------------

    /// <summary>Applique un thème d'aperçu, notifie la vue et persiste le choix.</summary>
    [RelayCommand]
    public void ChangePreset(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId) || string.Equals(presetId, ActivePresetId, StringComparison.Ordinal)) return;

        var previous = ActivePresetId;
        ActivePresetId = presetId;
        try
        {
            if (CurrentDocument is not null)
                CurrentHtml = HtmlRenderer.RenderToHtml(CurrentDocument, _stylePresetService.GetPreset(presetId), isStudentVersion: IsStudentView);

            try { _preferencesStore?.SaveActivePresetId(presetId); } catch { /* persistance non critique */ }

            PresetChanged?.Invoke(this, presetId);
            SetStatus($"Thème « {ResolvePresetLabel(presetId)} » appliqué à l'aperçu.", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            ActivePresetId = previous;
            SetStatus($"Impossible d'appliquer le thème : {ex.Message}", StatusSeverity.Error);
        }
    }

    private string ResolvePresetLabel(string presetId) =>
        AvailablePresets.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.Ordinal))?.Label ?? presetId;

    // ------------------------------------------------------------------
    // Zoom (persisté)
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    public void ZoomIn() => SetZoom(ZoomFactor + ZoomStep);

    private bool CanZoomIn() => ZoomFactor < MaxZoom - 0.001;

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    public void ZoomOut() => SetZoom(ZoomFactor - ZoomStep);

    private bool CanZoomOut() => ZoomFactor > MinZoom + 0.001;

    [RelayCommand]
    public void ResetZoom() => SetZoom(1.0);

    /// <summary>Applique un facteur de zoom précis (menu des préréglages de zoom).</summary>
    [RelayCommand]
    public void SetZoom(double value)
    {
        var clamped = Math.Round(Math.Clamp(value, MinZoom, MaxZoom), 2);
        if (Math.Abs(clamped - ZoomFactor) < 0.001) return;

        ZoomFactor = clamped;
        try { _preferencesStore?.SaveZoomFactor(clamped); } catch { /* persistance non critique */ }
        SetStatus($"Zoom de l'aperçu : {clamped:P0}.", StatusSeverity.Info);
    }

    // ------------------------------------------------------------------
    // Annuler / Rétablir (profondeur 10) — Ctrl+Z / Ctrl+Y
    // ------------------------------------------------------------------

    /// <summary>Capture un instantané du document AVANT une modification.</summary>
    public void PushSnapshot(string label)
    {
        if (CurrentDocument is null) return;

        _undoStack.Add(new DocumentSnapshot(CurrentDocument, CurrentHtml, label, DateTimeOffset.Now));
        if (_undoStack.Count > MaxHistoryDepth) _undoStack.RemoveAt(0);

        // Toute nouvelle modification invalide la pile « rétablir ».
        _redoStack.Clear();
        RefreshHistoryState();
    }

    /// <summary>Ctrl+Z — Restaure l'instantané précédent.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo()
    {
        if (_undoStack.Count == 0 || CurrentDocument is null) return;

        var snapshot = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(new DocumentSnapshot(CurrentDocument, CurrentHtml, snapshot.Label, DateTimeOffset.Now));

        ApplySnapshot(snapshot);
        RefreshHistoryState();
        SetStatus($"Annulation : {snapshot.Label}.", StatusSeverity.Info);
    }

    /// <summary>Ctrl+Y — Rétablit l'instantané annulé.</summary>
    [RelayCommand(CanExecute = nameof(CanRedo))]
    public void Redo()
    {
        if (_redoStack.Count == 0 || CurrentDocument is null) return;

        var snapshot = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(new DocumentSnapshot(CurrentDocument, CurrentHtml, snapshot.Label, DateTimeOffset.Now));

        ApplySnapshot(snapshot);
        RefreshHistoryState();
        SetStatus($"Rétablissement : {snapshot.Label}.", StatusSeverity.Info);
    }

    private void ApplySnapshot(DocumentSnapshot snapshot)
    {
        CurrentDocument = snapshot.Document;
        CurrentHtml = snapshot.Html;
        ComputeDocumentStats();
    }

    private void RefreshHistoryState()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
        UndoDescription = CanUndo ? _undoStack[^1].Label : string.Empty;
        RedoDescription = CanRedo ? _redoStack[^1].Label : string.Empty;
    }

    // ------------------------------------------------------------------
    // Export & impression — Ctrl+Maj+E · Ctrl+Maj+W · Ctrl+P
    // ------------------------------------------------------------------

    /// <summary>Ctrl+Maj+E — Exporte le document au format PDF.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    public async Task ExportPdfAsync() => await RunExportAsync(ExportKind.Pdf);

    /// <summary>Ctrl+Maj+W — Exporte le document au format Word (.docx).</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    public async Task ExportDocxAsync() => await RunExportAsync(ExportKind.Docx);

    /// <summary>Exporte le document au format RTF.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    public async Task ExportRtfAsync() => await RunExportAsync(ExportKind.Rtf);

    private async Task RunExportAsync(ExportKind kind)
    {
        if (CurrentDocument is null) return;

        var suggestedFileName = BuildSuggestedFileName(kind);

        // Sans service de flux : la vue prend le relais (sélecteur de fichier).
        if (_exportWorkflow is null && kind != ExportKind.Rtf)
        {
            ExportRequested?.Invoke(this, new ExportRequestedEventArgs(kind, CurrentDocument, CurrentHtml, suggestedFileName));
            return;
        }

        IsBusy = true;
        SetStatus(kind switch
        {
            ExportKind.Pdf => "Export PDF en cours…",
            ExportKind.Docx => "Export Word en cours…",
            _ => "Export RTF en cours…"
        }, StatusSeverity.Info);
        try
        {
            var path = kind switch
            {
                ExportKind.Pdf => await _exportWorkflow!.ExportPdfAsync(CurrentDocument, CurrentHtml, suggestedFileName, CancellationToken.None),
                ExportKind.Docx => await _exportWorkflow!.ExportDocxAsync(CurrentDocument, suggestedFileName, CancellationToken.None),
                _ => await ExportRtfDirectAsync(CurrentDocument, suggestedFileName, CancellationToken.None)
            };

            if (path is not null)
            {
                LastExportedFilePath = path;
                var fileName = Path.GetFileName(path);
                ExportSuccessMessage = kind switch
                {
                    ExportKind.Pdf => $"Document PDF prêt : {fileName}",
                    ExportKind.Docx => $"Document Word prêt : {fileName}",
                    _ => $"Document RTF prêt : {fileName}"
                };
                ShowExportSuccessBanner = true;
                SetStatus($"Document exporté : {path}", StatusSeverity.Success);
            }
            else
            {
                SetStatus("Export annulé.", StatusSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Échec de l'export : {ErrorMessageTranslator.ToUserFriendlyMessage(ex)}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Ouvre le dernier fichier exporté avec l'application par défaut de Windows.</summary>
    [RelayCommand]
    public void OpenLastExportedFile()
    {
        if (string.IsNullOrWhiteSpace(LastExportedFilePath) || !File.Exists(LastExportedFilePath)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LastExportedFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Impossible d'ouvrir le fichier : {ex.Message}", StatusSeverity.Warning);
        }
    }

    /// <summary>Ouvre l'Explorateur Windows et sélectionne le dernier fichier exporté.</summary>
    [RelayCommand]
    public void OpenLastExportedFolder()
    {
        if (string.IsNullOrWhiteSpace(LastExportedFilePath) || !File.Exists(LastExportedFilePath)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{LastExportedFilePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Impossible d'ouvrir le dossier : {ex.Message}", StatusSeverity.Warning);
        }
    }

    /// <summary>Ferme la bannière d'export réussi.</summary>
    [RelayCommand]
    public void DismissExportSuccessBanner()
    {
        ShowExportSuccessBanner = false;
    }

    /// <summary>Ctrl+P — Imprime le document courant.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    public async Task PrintAsync()
    {
        if (CurrentDocument is null) return;

        if (_exportWorkflow is null)
        {
            PrintRequested?.Invoke(this, new PrintRequestedEventArgs(CurrentHtml, CurrentDocument.Metadata.Title));
            return;
        }

        IsBusy = true;
        SetStatus("Préparation de l'impression…", StatusSeverity.Info);
        try
        {
            await _exportWorkflow.PrintAsync(CurrentHtml, CurrentDocument.Metadata.Title, CancellationToken.None);
            SetStatus("Document envoyé vers l'impression.", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"Impression impossible : {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Copie le texte brut du document dans le presse-papiers (via la vue).</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    public void CopyPlainText()
    {
        if (CurrentDocument is null) return;

        var text = _plainTextCache.Length > 0 ? _plainTextCache : ExtractPlainText(CurrentDocument);
        CopyRequested?.Invoke(this, new CopyRequestedEventArgs(text));
        SetStatus("Texte copié dans le presse-papiers.", StatusSeverity.Success);
    }

    // ------------------------------------------------------------------
    // Recherche — Ctrl+F · Échap
    // ------------------------------------------------------------------

    /// <summary>Ctrl+F — Ouvre / ferme la barre de recherche.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    public void ToggleFindBar()
    {
        IsFindBarVisible = !IsFindBarVisible;
        if (!IsFindBarVisible)
        {
            FindQuery = string.Empty;
            SetStatus("Recherche fermée.", StatusSeverity.Info);
        }
        else
        {
            SetStatus("Recherche dans le document — saisissez au moins 2 caractères.", StatusSeverity.Info);
        }
    }

    partial void OnFindQueryChanged(string value) => RefreshFindMatches();

    /// <summary>Recompte les occurrences et demande à la vue de faire défiler vers la suivante.</summary>
    [RelayCommand(CanExecute = nameof(IsFindBarVisible))]
    public void FindNext()
    {
        RefreshFindMatches();
        // La mise en surbrillance et le défilement sont gérés par la vue (WebView2).
    }

    private void RefreshFindMatches()
    {
        var query = FindQuery?.Trim() ?? string.Empty;
        if (query.Length < 2 || _plainTextCache.Length == 0)
        {
            FindMatchCount = 0;
            FindStatusText = query.Length == 0 ? string.Empty : "Saisissez au moins 2 caractères.";
            return;
        }

        FindMatchCount = CountOccurrences(_plainTextCache, query);
        FindStatusText = FindMatchCount switch
        {
            0 => "Aucune occurrence trouvée.",
            1 => "1 occurrence trouvée.",
            _ => $"{FindMatchCount} occurrences trouvées."
        };
    }

    /// <summary>Échap — Ferme la barre de recherche si elle est ouverte.</summary>
    [RelayCommand]
    public void HandleEscape()
    {
        if (IsFindBarVisible) ToggleFindBar();
    }

    // ------------------------------------------------------------------
    // Diagnostic — archive ZIP
    // ------------------------------------------------------------------

    /// <summary>Compile un rapport de diagnostic et l'exporte en archive ZIP.</summary>
    [RelayCommand]
    public async Task ExportDiagnosticsAsync()
    {
        if (_diagnosticZipExporter is null)
        {
            SetStatus("Le service d'export de diagnostic n'est pas configuré.", StatusSeverity.Warning);
            return;
        }

        IsBusy = true;
        SetStatus("Compilation du rapport de diagnostic…", StatusSeverity.Info);
        try
        {
            var entries = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Horodatage UTC"] = DateTime.UtcNow.ToString("o"),
                ["Version de l'application"] = typeof(ResultViewModel).Assembly.GetName().Version?.ToString() ?? "inconnue",
                ["Système"] = Environment.OSVersion.VersionString,
                ["Document ouvert"] = CurrentDocument?.Metadata.Title ?? "(aucun)",
                ["Niveau"] = CurrentDocument?.Metadata.ClassLevel ?? "—",
                ["Matière"] = CurrentDocument?.Metadata.Subject ?? "—",
                ["Thème d'aperçu"] = ActivePresetId,
                ["Facteur de zoom"] = ZoomFactor.ToString("0.00"),
                ["Nombre de sections"] = BlockCount.ToString(),
                ["Nombre de mots"] = WordCount.ToString(),
                ["Profondeur d'historique (annulation)"] = _undoStack.Count.ToString(),
                ["Dernier message d'état"] = StatusMessage
            };

            var path = await _diagnosticZipExporter.ExportAsync(entries, CancellationToken.None);
            SetStatus($"Rapport de diagnostic exporté : {path}", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"Échec de l'export de diagnostic : {ErrorMessageTranslator.ToUserFriendlyMessage(ex)}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ------------------------------------------------------------------
    // Aides internes
    // ------------------------------------------------------------------

    private void ComputeDocumentStats()
    {
        if (CurrentDocument is null)
        {
            _plainTextCache = string.Empty;
            WordCount = 0;
            BlockCount = 0;
            EstimatedReadingMinutes = 0;
            return;
        }

        _plainTextCache = ExtractPlainText(CurrentDocument);
        BlockCount = CurrentDocument.Blocks.Count;
        WordCount = _plainTextCache.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        EstimatedReadingMinutes = Math.Max(1, (int)Math.Ceiling(WordCount / 200.0));
    }

    private string BuildSuggestedFileName(ExportKind kind)
    {
        var title = CurrentDocument?.Metadata.Title ?? "document";
        foreach (var c in Path.GetInvalidFileNameChars()) title = title.Replace(c, ' ');
        title = string.Join(' ', title.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (title.Length == 0) title = "document";

        var extension = kind switch
        {
            ExportKind.Pdf => ".pdf",
            ExportKind.Docx => ".docx",
            _ => ".rtf"
        };
        return $"{title}{extension}";
    }

    private async Task<string?> ExportRtfDirectAsync(GeneratedDocument document, string suggestedFileName, CancellationToken cancellationToken)
    {
        var targetDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, suggestedFileName);
        var bytes = _rtfWriter.ExportRtfBytes(document);
        await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
        return targetPath;
    }

    public void SetStatus(string message, StatusSeverity severity)
    {
        StatusMessage = message;
        CurrentStatusSeverity = severity;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string ExtractPlainText(GeneratedDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine(doc.Metadata.Title);
        if (!string.IsNullOrEmpty(doc.Metadata.Subtitle)) sb.AppendLine(doc.Metadata.Subtitle);
        foreach (var block in doc.Blocks)
        {
            if (block is HeadingBlock h) sb.AppendLine(string.Join(" ", h.Runs.Select(r => r.Text)));
            else if (block is ParagraphBlock p) sb.AppendLine(string.Join(" ", p.Runs.Select(r => r.Text)));
        }
        return sb.ToString();
    }

    /// <summary>Instantané immuable du document pour l'historique annuler/rétablir.</summary>
    private sealed record DocumentSnapshot(GeneratedDocument Document, string Html, string Label, DateTimeOffset CapturedAt);
}
