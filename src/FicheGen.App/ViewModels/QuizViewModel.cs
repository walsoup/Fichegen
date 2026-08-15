// ============================================================================
//  FicheGen — QuizViewModel
//  Formulaire de génération de quiz (QCM, Vrai/Faux, réponses courtes).
//  Propriétés partielles (WinRT AOT) · ObservableValidator · Brouillon auto
// ============================================================================

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

/// <summary>
/// Vue-modèle du formulaire « Quiz ».
/// Clavier : Ctrl+Entrée (générer) · Échap (annuler).
/// </summary>
public partial class QuizViewModel : ObservableValidator
{
    private const string DraftKey = "quiz";
    private static readonly TimeSpan DraftDebounce = TimeSpan.FromMilliseconds(1200);

    private static readonly HashSet<string> DraftTrackedProperties = new(StringComparer.Ordinal)
    {
        nameof(ClassLevel), nameof(Subject), nameof(Topic), nameof(QuestionCount),
        nameof(DurationMinutes), nameof(IncludeMultipleChoice), nameof(IncludeTrueFalse),
        nameof(IncludeShortAnswer), nameof(IncludeAnswerKey), nameof(UseDyslexiaFont),
        nameof(GuideFilePath)
    };

    private readonly GenerationOrchestrator _orchestrator;
    private readonly ISettingsStore _settingsStore;
    private readonly ICredentialStore? _credentialStore;
    private readonly IHistoryRepository _historyRepository;
    private readonly StylePresetService _stylePresetService;
    private readonly IDraftStore? _draftStore;
    private readonly IReadinessService? _readinessService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly DispatcherQueueTimer? _draftTimer;
    private readonly DispatcherQueueTimer? _elapsedTimer;

    private CancellationTokenSource? _cts;
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
    [Required(ErrorMessage = "Le sujet du quiz est obligatoire.")]
    [MinLength(3, ErrorMessage = "Le sujet doit contenir au moins 3 caractères.")]
    [MaxLength(200, ErrorMessage = "Le sujet ne peut pas dépasser 200 caractères.")]
    public partial string Topic { get; set; }

    [ObservableProperty]
    [Range(3, 50, ErrorMessage = "Le nombre de questions doit être compris entre 3 et 50.")]
    [NotifyPropertyChangedFor(nameof(QuizSummaryText))]
    public partial int QuestionCount { get; set; }

    [ObservableProperty]
    [Range(5, 90, ErrorMessage = "La durée doit être comprise entre 5 et 90 minutes.")]
    [NotifyPropertyChangedFor(nameof(QuizSummaryText))]
    public partial int DurationMinutes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuestionTypesSummary))]
    public partial bool IncludeMultipleChoice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuestionTypesSummary))]
    public partial bool IncludeTrueFalse { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuestionTypesSummary))]
    public partial bool IncludeShortAnswer { get; set; }

    [ObservableProperty]
    public partial bool IncludeAnswerKey { get; set; }

    [ObservableProperty]
    public partial bool UseDyslexiaFont { get; set; }

    [ObservableProperty]
    public partial string? GuideFilePath { get; set; }

    // ------------------------------------------------------------------
    // État d'exécution & erreurs personnalisées
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(GenerateQuizCommand))]
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
    [NotifyPropertyChangedFor(nameof(HasQuestionTypeError))]
    public partial string QuestionTypeError { get; set; }

    /// <summary><c>true</c> si aucun type de question n'est sélectionné.</summary>
    public bool HasQuestionTypeError => !string.IsNullOrEmpty(QuestionTypeError);

    [ObservableProperty]
    public partial bool HasRestoredDraft { get; set; }

    [ObservableProperty]
    public partial string DraftStatusMessage { get; set; }

    [ObservableProperty]
    public partial ResultViewModel ResultViewModel { get; set; }

    // ------------------------------------------------------------------
    // Propriétés calculées (badges de synthèse)
    // ------------------------------------------------------------------

    /// <summary>Résumé des types de questions sélectionnés.</summary>
    public string QuestionTypesSummary
    {
        get
        {
            var parts = new List<string>(3);
            if (IncludeMultipleChoice) parts.Add("QCM");
            if (IncludeTrueFalse) parts.Add("Vrai / Faux");
            if (IncludeShortAnswer) parts.Add("Réponses courtes");
            return parts.Count == 0 ? "Aucun type sélectionné" : string.Join(" · ", parts);
        }
    }

    /// <summary>Résumé compact affiché dans l'en-tête du formulaire.</summary>
    public string QuizSummaryText => $"{QuestionCount} questions · {DurationMinutes} min";

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

    public IReadOnlyList<ShortcutHint> ShortcutHints { get; } = new[]
    {
        new ShortcutHint("Ctrl+Entrée", "Lancer la génération du quiz"),
        new ShortcutHint("Échap", "Annuler la génération en cours")
    };

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    public QuizViewModel(
        GenerationOrchestrator orchestrator,
        ISettingsStore settingsStore,
        IHistoryRepository historyRepository,
        StylePresetService stylePresetService,
        ResultViewModel resultViewModel,
        ICredentialStore? credentialStore = null,
        IDraftStore? draftStore = null,
        IReadinessService? readinessService = null)
    {
        _orchestrator = orchestrator;
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        _historyRepository = historyRepository;
        _stylePresetService = stylePresetService;
        ResultViewModel = resultViewModel;
        _draftStore = draftStore;
        _readinessService = readinessService;

        ClassLevel = "CM2";
        Subject = "Mathématiques";
        Topic = string.Empty;
        QuestionCount = 10;
        DurationMinutes = 15;
        IncludeMultipleChoice = true;
        IncludeTrueFalse = true;
        IncludeShortAnswer = false;
        IncludeAnswerKey = true;
        UseDyslexiaFont = false;
        StatusMessage = "Prêt à générer un quiz.";
        CurrentStatusSeverity = StatusSeverity.Info;
        ElapsedTimeText = string.Empty;
        QuestionTypeError = string.Empty;
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

    /// <summary>Ctrl+Entrée — Valide le formulaire puis génère le quiz.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    public async Task GenerateQuizAsync()
    {
        ValidateAllProperties();

        // Validation transversale : au moins un type de question.
        if (!IncludeMultipleChoice && !IncludeTrueFalse && !IncludeShortAnswer)
        {
            QuestionTypeError = "Sélectionnez au moins un type de question.";
            SetStatus($"⚠️ {QuestionTypeError}", StatusSeverity.Warning);
            return;
        }
        QuestionTypeError = string.Empty;

        if (HasErrors)
        {
            SetStatus(BuildValidationSummary(), StatusSeverity.Warning);
            return;
        }

        IsGenerating = true;
        ResultViewModel.IsBusy = true;
        ResultViewModel.CurrentHtml = string.Empty;
        ResultViewModel.SetStatus("Conception des questions et des distracteurs…", StatusSeverity.Info);
        SetStatus("Conception des questions et des distracteurs…", StatusSeverity.Info);
        ElapsedTimeText = "0,0 s";
        _generationStartedUtc = DateTimeOffset.UtcNow;
        _elapsedTimer?.Start();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            var appSettings = _settingsStore.GetSettings<AppSettings>();
            var config = BuildAiConfig(appSettings);

            var promptInstructions = "";
            if (IncludeAnswerKey)
            {
                promptInstructions = "Inclure un corrigé détaillé à la fin du quiz.";
            }

            var parameters = new QuizParameters(
                ClassLevel,
                Subject,
                Topic.Trim(),
                QuestionCount,
                DurationMinutes,
                IncludeMultipleChoice,
                IncludeTrueFalse,
                IncludeShortAnswer,
                promptInstructions
            );

            SetStatus("Rédaction du quiz par l'IA…", StatusSeverity.Info);
            ResultViewModel.SetStatus("Rédaction du quiz par l'IA…", StatusSeverity.Info);
            var result = await _orchestrator.GenerateQuizAsync(parameters, config, _cts.Token);
            var document = result.Document;
            var html = result.PreviewHtml;

            var preset = UseDyslexiaFont ? "dyslexie" : appSettings.Defaults.StylePresetId;
            ResultViewModel.LoadDocument(document, html, preset);

            var historyItem = new HistoryItem
            {
                Id = Guid.NewGuid().ToString(),
                Type = "quiz",
                Title = document.Metadata.Title,
                ClassLevel = document.Metadata.ClassLevel,
                Subject = document.Metadata.Subject,
                CreatedUtc = DateTime.UtcNow,
                IsFavorite = false,
                PlainText = document.Metadata.Title,
                Html = html,
                SourceJson = document.SourceJson,
                StylePresetId = appSettings.Defaults.StylePresetId
            };

            await _historyRepository.SaveAsync(historyItem);
            _readinessService?.ReportExecutionOutcome(true);
            SetStatus($"Quiz « {document.Metadata.Title} » généré en {result.Elapsed.TotalSeconds:F1} s.", StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Génération annulée par l'utilisateur.", StatusSeverity.Warning);
        }
        catch (Exception ex)
        {
            _readinessService?.ReportExecutionOutcome(false, ex);
            SetStatus(ErrorMessageTranslator.ToUserFriendlyMessage(ex), StatusSeverity.Error);
        }
        finally
        {
            _elapsedTimer?.Stop();
            IsGenerating = false;
            ResultViewModel.IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanGenerate() => !IsGenerating;

    /// <summary>Échap — Annule la génération en cours.</summary>
    [RelayCommand(CanExecute = nameof(IsGenerating))]
    public void CancelGeneration() => _cts?.Cancel();

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
                [nameof(Topic)] = Topic,
                [nameof(QuestionCount)] = QuestionCount.ToString(),
                [nameof(DurationMinutes)] = DurationMinutes.ToString(),
                [nameof(IncludeMultipleChoice)] = IncludeMultipleChoice ? "true" : "false",
                [nameof(IncludeTrueFalse)] = IncludeTrueFalse ? "true" : "false",
                [nameof(IncludeShortAnswer)] = IncludeShortAnswer ? "true" : "false",
                [nameof(IncludeAnswerKey)] = IncludeAnswerKey ? "true" : "false",
                [nameof(UseDyslexiaFont)] = UseDyslexiaFont ? "true" : "false",
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
                    if (fields.TryGetValue(nameof(Topic), out var to) && to is not null) Topic = to;
                    if (fields.TryGetValue(nameof(QuestionCount), out var qc) && int.TryParse(qc, out var count) && count is >= 3 and <= 50) QuestionCount = count;
                    if (fields.TryGetValue(nameof(DurationMinutes), out var du) && int.TryParse(du, out var minutes) && minutes is >= 5 and <= 90) DurationMinutes = minutes;
                    if (fields.TryGetValue(nameof(IncludeMultipleChoice), out var mc) && bool.TryParse(mc, out var qcm)) IncludeMultipleChoice = qcm;
                    if (fields.TryGetValue(nameof(IncludeTrueFalse), out var tf) && bool.TryParse(tf, out var vf)) IncludeTrueFalse = vf;
                    if (fields.TryGetValue(nameof(IncludeShortAnswer), out var sa) && bool.TryParse(sa, out var rc)) IncludeShortAnswer = rc;
                    if (fields.TryGetValue(nameof(IncludeAnswerKey), out var ak) && bool.TryParse(ak, out var answerKey)) IncludeAnswerKey = answerKey;
                    if (fields.TryGetValue(nameof(UseDyslexiaFont), out var df) && bool.TryParse(df, out var dyslexiaFont)) UseDyslexiaFont = dyslexiaFont;
                    if (fields.TryGetValue(nameof(GuideFilePath), out var gf)) GuideFilePath = gf;
                    ClearErrors();
                }
                finally
                {
                    _isRestoringDraft = false;
                }

                if (!string.IsNullOrWhiteSpace(Topic))
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
        (k, _) => ValueTask.FromResult(_credentialStore?.Get(k)));
}
