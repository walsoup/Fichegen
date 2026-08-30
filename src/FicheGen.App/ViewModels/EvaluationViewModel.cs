// ============================================================================
//  FicheGen — EvaluationViewModel
//  Formulaire de génération d'évaluations (sommatives / formatives).
//  Propriétés partielles (WinRT AOT) · ObservableValidator · Brouillon auto
// ============================================================================

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.App.Services;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Ai;
using FicheGen.Core.Documents;
using FicheGen.Core.Prompts;
using FicheGen.Core.Services;
using FicheGen.Core.Storage;
using FicheGen.Infrastructure.Services;
using Microsoft.UI.Dispatching;

namespace FicheGen.App.ViewModels;

/// <summary>Option de type d'évaluation exposée aux sélecteurs de la vue.</summary>
public sealed record EvaluationTypeOption(string Value, string Label, string Description);

/// <summary>
/// Vue-modèle du formulaire « Évaluation ».
/// Clavier : Ctrl+Entrée (générer) · Échap (annuler).
/// </summary>
public partial class EvaluationViewModel : ObservableValidator
{
    private const string DraftKey = "evaluation";
    private static readonly TimeSpan DraftDebounce = TimeSpan.FromMilliseconds(1200);

    private static readonly HashSet<string> DraftTrackedProperties = new(StringComparer.Ordinal)
    {
        nameof(ClassLevel), nameof(Subject), nameof(Topics), nameof(EvaluationType),
        nameof(DurationMinutes), nameof(DifficultyLevel), nameof(TotalPoints), nameof(GuideFilePath)
    };

    private readonly GenerationOrchestrator _orchestrator;
    private readonly ISettingsStore _settingsStore;
    private readonly ICredentialStore? _credentialStore;
    private readonly FicheGen.Core.Auth.IAuthService? _authService;
    private readonly IHistoryRepository _historyRepository;
    private readonly StylePresetService _stylePresetService;
    private readonly IDraftStore? _draftStore;
    private readonly IReadinessService? _readinessService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly DispatcherQueueTimer? _draftTimer;
    private readonly DispatcherQueueTimer? _elapsedTimer;

    private DateTimeOffset _generationStartedUtc;
    private bool _isRestoringDraft;

    // ------------------------------------------------------------------
    // Champs du formulaire (validés)
    // ------------------------------------------------------------------

    [ObservableProperty]
    public partial string ClassLevel { get; set; }

    [ObservableProperty]
    public partial string Subject { get; set; }

    [ObservableProperty]
    [Required(ErrorMessage = "Les sujets de l'évaluation sont obligatoires.")]
    [MinLength(3, ErrorMessage = "Indiquez au moins 3 caractères de sujets.")]
    [MaxLength(500, ErrorMessage = "Les sujets ne peuvent pas dépasser 500 caractères.")]
    public partial string Topics { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EvaluationSummaryText))]
    public partial string EvaluationType { get; set; } // "sommative" | "formative"

    [ObservableProperty]
    [Range(5, 180, ErrorMessage = "La durée doit être comprise entre 5 et 180 minutes.")]
    [NotifyPropertyChangedFor(nameof(EvaluationSummaryText))]
    public partial int DurationMinutes { get; set; }

    [ObservableProperty]
    [Range(1.0, 5.0, ErrorMessage = "La difficulté doit être comprise entre 1 et 5.")]
    [NotifyPropertyChangedFor(nameof(DifficultyLabel))]
    [NotifyPropertyChangedFor(nameof(DifficultyPercentText))]
    [NotifyPropertyChangedFor(nameof(EvaluationSummaryText))]
    public partial double DifficultyLevel { get; set; } // 1.0 - 5.0

    [ObservableProperty]
    [Range(5, 100, ErrorMessage = "Le barème doit être compris entre 5 et 100 points.")]
    [NotifyPropertyChangedFor(nameof(EvaluationSummaryText))]
    public partial int TotalPoints { get; set; }

    [ObservableProperty]
    public partial bool HasExerciseNotions { get; set; }

    [ObservableProperty]
    public partial bool HasCorrectionGuide { get; set; }

    [ObservableProperty]
    public partial bool HasDifferentiation { get; set; }

    [ObservableProperty]
    public partial bool UsePedagogicalGuide { get; set; }

    [ObservableProperty]
    public partial string? GuideFilePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentLengthLabel))]
    [NotifyPropertyChangedFor(nameof(DocumentLengthKey))]
    public partial int DocumentLengthIndex { get; set; } = 2;

    public string DocumentLengthKey => DocumentLengthIndex switch
    {
        0 => "Bref",
        1 => "Raccourci",
        2 => "Defaut",
        3 => "Long",
        4 => "Detaille",
        5 => "Exhaustif",
        _ => "Defaut"
    };

    public string DocumentLengthLabel => DocumentLengthIndex switch
    {
        0 => "Bref (1 page courte)",
        1 => "Raccourci (1 page)",
        2 => "Défaut (1-2 pages)",
        3 => "Long (2-3 pages)",
        4 => "Détaillé (3-4 pages)",
        5 => "Exhaustif (4+ pages)",
        _ => "Défaut"
    };

    // ------------------------------------------------------------------
    // État d'exécution
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(GenerateEvaluationCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelGenerationCommand))]
    public partial bool IsGenerating { get; set; }

    public bool IsIdle => !IsGenerating;

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial StatusSeverity CurrentStatusSeverity { get; set; }

    [ObservableProperty]
    public partial string ElapsedTimeText { get; set; }

    [ObservableProperty]
    public partial bool HasRestoredDraft { get; set; }

    [ObservableProperty]
    public partial string DraftStatusMessage { get; set; }

    [ObservableProperty]
    public partial ResultViewModel ResultViewModel { get; set; }

    // ------------------------------------------------------------------
    // Propriétés calculées (badges de synthèse)
    // ------------------------------------------------------------------

    /// <summary>Libellé lisible du niveau de difficulté.</summary>
    public string DifficultyLabel => DifficultyLevel switch
    {
        <= 1.5 => "Accessible",
        <= 2.5 => "Standard",
        <= 3.5 => "Intermédiaire",
        <= 4.5 => "Avancé",
        _ => "Exigeant"
    };

    public string DifficultyPercentText => $"{(int)Math.Round(DifficultyLevel * 20)} %";

    /// <summary>Résumé compact affiché dans l'en-tête du formulaire.</summary>
    public string EvaluationSummaryText =>
        $"{(string.Equals(EvaluationType, "formative", StringComparison.OrdinalIgnoreCase) ? "Formative" : "Sommative")} · {TotalPoints} points · {DurationMinutes} min · {DifficultyLabel}";

    // ------------------------------------------------------------------
    // Catalogues exposés à la vue
    // ------------------------------------------------------------------

    public IReadOnlyList<string> ClassLevels { get; } = new[]
    {
        "CP", "CE1", "CE2", "CM1", "CM2",
        "6e", "5e", "4e", "3e",
        "Seconde", "Première", "Terminale"
    };

    public IReadOnlyList<string> Subjects { get; } = new[]
    {
        "Français", "Mathématiques", "Histoire-Géographie", "Sciences",
        "Langues vivantes", "Physique-Chimie", "SVT", "Technologie",
        "EPS", "Arts plastiques", "Éducation musicale", "EMC"
    };

    public IReadOnlyList<EvaluationTypeOption> EvaluationTypes { get; } = new[]
    {
        new EvaluationTypeOption("sommative", "Évaluation sommative", "Bilan de fin de séquence, noté et sanctionné."),
        new EvaluationTypeOption("formative", "Évaluation formative", "Point d'étape rapide pour ajuster l'enseignement.")
    };

    public IReadOnlyList<ShortcutHint> ShortcutHints { get; } = new[]
    {
        new ShortcutHint("Ctrl+Entrée", "Lancer la génération de l'évaluation"),
        new ShortcutHint("Échap", "Annuler la génération en cours")
    };

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    public EvaluationViewModel(
        GenerationOrchestrator orchestrator,
        ISettingsStore settingsStore,
        IHistoryRepository historyRepository,
        StylePresetService stylePresetService,
        ResultViewModel resultViewModel,
        ICredentialStore? credentialStore = null,
        FicheGen.Core.Auth.IAuthService? authService = null,
        IDraftStore? draftStore = null,
        IReadinessService? readinessService = null)
    {
        _orchestrator = orchestrator;
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        _authService = authService;
        _historyRepository = historyRepository;
        _stylePresetService = stylePresetService;
        ResultViewModel = resultViewModel;
        _draftStore = draftStore;
        _readinessService = readinessService;

        ClassLevel = "CM2";
        Subject = "Mathématiques";
        Topics = string.Empty;
        EvaluationType = "sommative";
        DurationMinutes = 45;
        DifficultyLevel = 3.0;
        TotalPoints = 20;
        StatusMessage = "Prêt à générer une évaluation.";
        CurrentStatusSeverity = StatusSeverity.Info;
        ElapsedTimeText = string.Empty;
        DraftStatusMessage = string.Empty;

        var settings = _settingsStore.GetSettings<AppSettings>();
        if (!string.IsNullOrWhiteSpace(settings.Defaults.ClassLevel)) ClassLevel = settings.Defaults.ClassLevel;
        if (!string.IsNullOrWhiteSpace(settings.Defaults.Subject)) Subject = settings.Defaults.Subject;

        ClearErrors();

        try { _dispatcherQueue = DispatcherQueue.GetForCurrentThread(); } catch { _dispatcherQueue = null; }
        if (_dispatcherQueue is not null)
        {
            _draftTimer = _dispatcherQueue.CreateTimer();
            _draftTimer.Interval = DraftDebounce;
            _draftTimer.IsRepeating = false;
            _draftTimer.Tick += OnDraftTimerTick;

            _elapsedTimer = _dispatcherQueue.CreateTimer();
            _elapsedTimer.Interval = TimeSpan.FromMilliseconds(500);
            _elapsedTimer.IsRepeating = true;
            _elapsedTimer.Tick += OnElapsedTimerTick;
        }

        PropertyChanged += OnSelfPropertyChanged;
        _ = RestoreDraftSafelyAsync();
    }

    // ------------------------------------------------------------------
    // Commandes
    // ------------------------------------------------------------------

    /// <summary>Ctrl+Entrée — Valide le formulaire puis génère l'évaluation.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    public async Task GenerateEvaluationAsync()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            SetStatus(BuildValidationSummary(), StatusSeverity.Warning);
            return;
        }

        IsGenerating = true;
        var opId = ResultViewModel.BeginOperation("evaluation");
        var ct = ResultViewModel.GetActiveCancellationToken();
        ElapsedTimeText = "0,0 s";
        _generationStartedUtc = DateTimeOffset.UtcNow;
        _elapsedTimer?.Start();

        try
        {
            var appSettings = _settingsStore.GetSettings<AppSettings>();
            var config = BuildAiConfig(appSettings);

            var parameters = new EvalParameters(
                ClassLevel,
                Subject,
                Topics.Trim(),
                EvaluationType,
                TotalPoints,
                DifficultyLevel / 5.0,
                UsePedagogicalGuide: UsePedagogicalGuide,
                CurrentDate: DateTime.Now.ToString("yyyy-MM-dd"),
                Language: appSettings.Ui.Language,
                DocumentLength: DocumentLengthKey
            );

            ResultViewModel.SetGenerationPhase(1, "Structuration de l'évaluation et du barème…", opId);
            SetStatus("Structuration de l'évaluation et du barème…", StatusSeverity.Info);

            FicheGen.Core.Services.GenerationResult result;
            if (appSettings.Ui.EnableStreaming)
            {
                ResultViewModel.SetGenerationPhase(2, "Conception des compétences et exercices…", opId);
                var progress = new Progress<string>(chunk =>
                {
                    ResultViewModel.AppendStreamedChunk(chunk, opId);
                });
                result = await _orchestrator.GenerateEvaluationStreamingAsync(parameters, config, appSettings.Folders.GuidesDir, progress, ct);
            }
            else
            {
                ResultViewModel.SetGenerationPhase(3, "Rédaction des exercices par l'IA…", opId);
                result = await _orchestrator.GenerateEvaluationAsync(parameters, config, appSettings.Folders.GuidesDir, ct);
            }

            if (!ResultViewModel.IsActiveOperation(opId)) return;

            ResultViewModel.SetGenerationPhase(4, "Calcul des points et mise en page…", opId);
            var document = result.Document;
            var html = result.PreviewHtml;

            ResultViewModel.LoadDocument(document, html, appSettings.Defaults.StylePresetId, opId);

            var historyItem = new HistoryItem
            {
                Id = Guid.NewGuid().ToString(),
                Type = "evaluation",
                Title = document.Metadata.Title,
                ClassLevel = document.Metadata.ClassLevel,
                Subject = document.Metadata.Subject,
                CreatedUtc = DateTime.UtcNow,
                IsFavorite = false,
                PlainText = document.ToPlainText(),
                Html = html,
                SourceJson = document.SourceJson ?? System.Text.Json.JsonSerializer.Serialize(document),
                StylePresetId = appSettings.Defaults.StylePresetId,
                RawPrompt = null,
                RawResponse = null
            };

            await _historyRepository.SaveAsync(historyItem);
            _readinessService?.ReportExecutionOutcome(true);
            SetStatus($"Évaluation « {document.Metadata.Title} » générée en {result.Elapsed.TotalSeconds:F1} s.", StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            if (ResultViewModel.IsActiveOperation(opId))
            {
                ResultViewModel.ReportOperationCancelled(opId);
            }
            SetStatus("Génération annulée par l'utilisateur.", StatusSeverity.Warning);
        }
        catch (Exception ex)
        {
            _readinessService?.ReportExecutionOutcome(false, ex);
            var friendlyMessage = ErrorMessageTranslator.ToUserFriendlyMessage(ex);
            if (ResultViewModel.IsActiveOperation(opId))
            {
                ResultViewModel.ReportOperationFailure(opId, ex);
            }
            SetStatus($"Échec de la génération : {friendlyMessage}", StatusSeverity.Error);
        }
        finally
        {
            _elapsedTimer?.Stop();
            IsGenerating = false;
            ResultViewModel.EndOperation(opId);
        }
    }

    private bool CanGenerate() => !IsGenerating;

    /// <summary>Échap — Annule la génération en cours.</summary>
    [RelayCommand(CanExecute = nameof(IsGenerating))]
    public void CancelGeneration() => ResultViewModel.CancelActiveOperation();

    public System.Windows.Input.ICommand CancelCommand => CancelGenerationCommand;

    /// <summary>Masque la bannière « brouillon restauré ».</summary>
    [RelayCommand]
    public void DismissRestoredDraft() => HasRestoredDraft = false;

    /// <summary>Efface le brouillon persisté de ce formulaire.</summary>
    [RelayCommand]
    public async Task ClearDraftAsync()
    {
        HasRestoredDraft = false;
        DraftStatusMessage = string.Empty;
        if (_draftStore is null) return;
        try
        {
            await _draftStore.ClearDraftAsync(DraftKey);
            SetStatus("Brouillon effacé.", StatusSeverity.Info);
        }
        catch
        {
            // Silencieux par conception.
        }
    }

    // ------------------------------------------------------------------
    // Brouillon automatique (anti-rebond)
    // ------------------------------------------------------------------

    private void OnSelfPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isRestoringDraft || e.PropertyName is null) return;
        if (DraftTrackedProperties.Contains(e.PropertyName)) ScheduleDraftSave();
    }

    private void ScheduleDraftSave()
    {
        if (_draftStore is null || _draftTimer is null || IsGenerating) return;
        _draftTimer.Stop();
        _draftTimer.Start();
    }

    private async void OnDraftTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        await SaveDraftSafelyAsync();
    }

    private async Task SaveDraftSafelyAsync()
    {
        if (_draftStore is null || IsGenerating) return;
        try
        {
            var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [nameof(ClassLevel)] = ClassLevel,
                [nameof(Subject)] = Subject,
                [nameof(Topics)] = Topics,
                [nameof(EvaluationType)] = EvaluationType,
                [nameof(DurationMinutes)] = DurationMinutes.ToString(),
                [nameof(DifficultyLevel)] = DifficultyLevel.ToString(CultureInfo.InvariantCulture),
                [nameof(TotalPoints)] = TotalPoints.ToString(),
                [nameof(GuideFilePath)] = GuideFilePath
            };
            await _draftStore.SaveDraftAsync(DraftKey, fields);
            DraftStatusMessage = $"Brouillon enregistré automatiquement à {DateTime.Now:HH:mm:ss}.";
        }
        catch
        {
            // La persistance du brouillon ne doit jamais interrompre la saisie.
        }
    }

    private async Task RestoreDraftSafelyAsync()
    {
        if (_draftStore is null) return;
        try
        {
            var fields = await _draftStore.LoadDraftAsync(DraftKey);
            if (fields is null || fields.Count == 0) return;

            void Apply()
            {
                _isRestoringDraft = true;
                try
                {
                    if (fields.TryGetValue(nameof(ClassLevel), out var cl) && !string.IsNullOrWhiteSpace(cl)) ClassLevel = cl;
                    if (fields.TryGetValue(nameof(Subject), out var su) && !string.IsNullOrWhiteSpace(su)) Subject = su;
                    if (fields.TryGetValue(nameof(Topics), out var to) && to is not null) Topics = to;
                    if (fields.TryGetValue(nameof(EvaluationType), out var et) && !string.IsNullOrWhiteSpace(et)) EvaluationType = et;
                    if (fields.TryGetValue(nameof(DurationMinutes), out var du) && int.TryParse(du, out var minutes) && minutes is >= 5 and <= 180) DurationMinutes = minutes;
                    if (fields.TryGetValue(nameof(DifficultyLevel), out var dl) && double.TryParse(dl, NumberStyles.Float, CultureInfo.InvariantCulture, out var difficulty) && difficulty is >= 1.0 and <= 5.0) DifficultyLevel = difficulty;
                    if (fields.TryGetValue(nameof(TotalPoints), out var tp) && int.TryParse(tp, out var points) && points is >= 5 and <= 100) TotalPoints = points;
                    if (fields.TryGetValue(nameof(GuideFilePath), out var gf)) GuideFilePath = gf;
                    ClearErrors();
                }
                finally
                {
                    _isRestoringDraft = false;
                }

                if (!string.IsNullOrWhiteSpace(Topics))
                {
                    HasRestoredDraft = true;
                    DraftStatusMessage = "Brouillon restauré. Vous pouvez reprendre là où vous étiez.";
                }
            }

            if (_dispatcherQueue is not null) _dispatcherQueue.TryEnqueue(Apply);
            else Apply();
        }
        catch
        {
            // Restauration silencieuse.
        }
    }

    // ------------------------------------------------------------------
    // Chronomètre & aides internes
    // ------------------------------------------------------------------

    private void OnElapsedTimerTick(DispatcherQueueTimer sender, object args)
    {
        var elapsed = DateTimeOffset.UtcNow - _generationStartedUtc;
        ElapsedTimeText = $"{elapsed.TotalSeconds:F1} s";
    }

    private void SetStatus(string message, StatusSeverity severity)
    {
        StatusMessage = message;
        CurrentStatusSeverity = severity;
    }

    private string BuildValidationSummary()
    {
        var errors = GetErrors()
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToList();

        return errors.Count == 0
            ? "⚠️ Veuillez vérifier le formulaire."
            : "⚠️ " + string.Join(" ", errors);
    }

    private AiRequestConfig BuildAiConfig(AppSettings appSettings) => new(
        appSettings.Ai.GlobalProvider,
        appSettings.Ai.Models,
        appSettings.Ai.RoutingOverrides.ToDictionary(k => k.Key, v => new RoutingOverride(v.Value.Provider, v.Value.Model)),
        appSettings.Ai.ProxyBaseUrl,
        appSettings.Ai.Vertex.Project,
        appSettings.Ai.Vertex.Region,
        new Dictionary<string, double> { { "generation", appSettings.Ai.Temperatures.Generation } },
        async (k, ct) =>
        {
            if (k == "supabase_access_token" && _authService != null)
            {
                return await _authService.GetValidTokenAsync(ct).ConfigureAwait(false);
            }
            return _credentialStore?.Get(k);
        });
}
