// ============================================================================
//  FicheGen — FicheFormViewModel
//  Formulaire de génération de fiches pédagogiques.
//  Fluent 2 · CommunityToolkit.Mvvm (propriétés partielles — compatibilité
//  WinRT AOT) · ObservableValidator · Brouillon automatique · Purge historique
// ============================================================================

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
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

/// <summary>Gravité d'un message d'état affiché dans l'interface (InfoBar / badge).</summary>
public enum StatusSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Service de maintenance de l'historique : purge les éléments plus anciens que la
/// durée de rétention configurée et retourne le nombre d'éléments supprimés.
/// </summary>
public interface IHistoryRetentionService
{
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Vue-modèle du formulaire « Fiche pédagogique ».
/// Clavier : Ctrl+Entrée (générer) · Échap (annuler).
/// </summary>
public partial class FicheFormViewModel : ObservableValidator
{
    private const string DraftKey = "fiche";
    private static readonly TimeSpan DraftDebounce = TimeSpan.FromMilliseconds(1200);

    private static readonly HashSet<string> DraftTrackedProperties = new(StringComparer.Ordinal)
    {
        nameof(ClassLevel), nameof(Subject), nameof(Topic),
        nameof(DurationMinutes), nameof(AdditionalInstructions), nameof(UsePedagogicalGuide)
    };

    private readonly GenerationOrchestrator _orchestrator;
    private readonly ISettingsStore _settingsStore;
    private readonly ICredentialStore? _credentialStore;
    private readonly IHistoryRepository _historyRepository;
    private readonly StylePresetService _stylePresetService;
    private readonly IDraftStore? _draftStore;
    private readonly IHistoryRetentionService? _historyRetentionService;
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
    [Required(ErrorMessage = "Le sujet de la leçon est obligatoire.")]
    [MinLength(3, ErrorMessage = "Le sujet doit contenir au moins 3 caractères.")]
    [MaxLength(200, ErrorMessage = "Le sujet ne peut pas dépasser 200 caractères.")]
    [NotifyPropertyChangedFor(nameof(TopicCharacterCount))]
    public partial string Topic { get; set; }

    /// <summary>Nombre de caractères du sujet (badge compteur dans la vue).</summary>
    public int TopicCharacterCount => Topic?.Length ?? 0;

    [ObservableProperty]
    [Range(5, 240, ErrorMessage = "La durée doit être comprise entre 5 et 240 minutes.")]
    public partial int DurationMinutes { get; set; }

    [ObservableProperty]
    [MaxLength(2000, ErrorMessage = "Les consignes additionnelles ne peuvent pas dépasser 2 000 caractères.")]
    [NotifyPropertyChangedFor(nameof(AdditionalInstructionsCount))]
    public partial string AdditionalInstructions { get; set; }

    /// <summary>Nombre de caractères des consignes additionnelles.</summary>
    public int AdditionalInstructionsCount => AdditionalInstructions?.Length ?? 0;

    [ObservableProperty]
    public partial bool UsePedagogicalGuide { get; set; }

    [ObservableProperty]
    public partial string? GuideFilePath { get; set; }

    // ------------------------------------------------------------------
    // État d'exécution
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(GenerateFicheCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelGenerationCommand))]
    public partial bool IsGenerating { get; set; }

    /// <summary><c>true</c> lorsqu'aucune génération n'est en cours.</summary>
    public bool IsIdle => !IsGenerating;

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial StatusSeverity CurrentStatusSeverity { get; set; }

    [ObservableProperty]
    public partial string ElapsedTimeText { get; set; }

    // ------------------------------------------------------------------
    // Brouillon & maintenance
    // ------------------------------------------------------------------

    [ObservableProperty]
    public partial bool HasRestoredDraft { get; set; }

    [ObservableProperty]
    public partial string DraftStatusMessage { get; set; }

    [ObservableProperty]
    public partial string RetentionNotice { get; set; }

    [ObservableProperty]
    public partial ResultViewModel ResultViewModel { get; set; }

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

    /// <summary>Modèles de démarrage rapide de sujets.</summary>
    public IReadOnlyList<string> TopicTemplates { get; } = new[]
    {
        "Les fractions simples",
        "La lecture suivie d'un récit",
        "La Révolution française",
        "Le cycle de l'eau",
        "Les accords dans le groupe nominal",
        "La proportionnalité"
    };

    public IReadOnlyList<ShortcutHint> ShortcutHints { get; } = new[]
    {
        new ShortcutHint("Ctrl+Entrée", "Lancer la génération de la fiche"),
        new ShortcutHint("Échap", "Annuler la génération en cours")
    };

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    public FicheFormViewModel(
        GenerationOrchestrator orchestrator,
        ISettingsStore settingsStore,
        IHistoryRepository historyRepository,
        StylePresetService stylePresetService,
        ResultViewModel resultViewModel,
        ICredentialStore? credentialStore = null,
        IDraftStore? draftStore = null,
        IHistoryRetentionService? historyRetentionService = null,
        IReadinessService? readinessService = null)
    {
        _orchestrator = orchestrator;
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        _historyRepository = historyRepository;
        _stylePresetService = stylePresetService;
        ResultViewModel = resultViewModel;
        _draftStore = draftStore;
        _historyRetentionService = historyRetentionService;
        _readinessService = readinessService;

        // Les propriétés partielles ne peuvent pas avoir d'initialiseur :
        // les valeurs par défaut sont donc fixées ici.
        ClassLevel = "CM2";
        Subject = "Mathématiques";
        Topic = string.Empty;
        DurationMinutes = 60;
        AdditionalInstructions = string.Empty;
        UsePedagogicalGuide = true;
        StatusMessage = "Prêt à générer une fiche pédagogique.";
        CurrentStatusSeverity = StatusSeverity.Info;
        ElapsedTimeText = string.Empty;
        DraftStatusMessage = string.Empty;
        RetentionNotice = string.Empty;

        var settings = _settingsStore.GetSettings<AppSettings>();
        if (!string.IsNullOrWhiteSpace(settings.Defaults.ClassLevel)) ClassLevel = settings.Defaults.ClassLevel;
        if (!string.IsNullOrWhiteSpace(settings.Defaults.Subject)) Subject = settings.Defaults.Subject;

        // Formulaire vierge : aucune erreur affichée avant la première validation explicite.
        ClearErrors();

        // Minuteurs UI (anti-rebond du brouillon + chronomètre de génération).
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

        // Tâches d'arrière-plan : restauration du brouillon + purge de rétention.
        _ = RestoreDraftSafelyAsync();
        _ = RunRetentionSweepSafelyAsync();
    }

    // ------------------------------------------------------------------
    // Commandes
    // ------------------------------------------------------------------

    /// <summary>Ctrl+Entrée — Valide le formulaire puis génère la fiche.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    public async Task GenerateFicheAsync()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            SetStatus(BuildValidationSummary(), StatusSeverity.Warning);
            return;
        }

        IsGenerating = true;
        ResultViewModel.IsBusy = true;
        ResultViewModel.CurrentHtml = string.Empty;
        ResultViewModel.ResetStreaming();
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

            var parameters = new FicheParameters(
                ClassLevel,
                Subject,
                Topic.Trim(),
                DurationMinutes,
                AdditionalInstructions,
                UsePedagogicalGuide
            );

            ResultViewModel.SetGenerationPhase(1, "Analyse du sujet et préparation des objectifs…");
            SetStatus("Analyse du sujet et préparation de la requête…", StatusSeverity.Info);

            FicheGen.Core.Services.GenerationResult result;
            if (appSettings.Ui.EnableStreaming)
            {
                ResultViewModel.SetGenerationPhase(2, "Élaboration de la structure pédagogique…");
                var progress = new Progress<string>(chunk =>
                {
                    ResultViewModel.AppendStreamedChunk(chunk);
                });
                result = await _orchestrator.GenerateFicheStreamingAsync(parameters, config, appSettings.Folders.GuidesDir, progress, _cts.Token);
            }
            else
            {
                ResultViewModel.SetGenerationPhase(3, "Rédaction de la fiche pédagogique par l'IA…");
                result = await _orchestrator.GenerateFicheAsync(parameters, config, appSettings.Folders.GuidesDir, _cts.Token);
            }

            ResultViewModel.SetGenerationPhase(4, "Mise en page et finalisation du document…");
            var document = result.Document;
            var html = result.PreviewHtml;

            ResultViewModel.LoadDocument(document, html, appSettings.Defaults.StylePresetId);

            var historyItem = new HistoryItem
            {
                Id = Guid.NewGuid().ToString(),
                Type = "fiche",
                Title = document.Metadata.Title,
                ClassLevel = document.Metadata.ClassLevel,
                Subject = document.Metadata.Subject,
                CreatedUtc = DateTime.UtcNow,
                IsFavorite = false,
                PlainText = GetPlainText(document),
                Html = html,
                SourceJson = document.SourceJson,
                StylePresetId = appSettings.Defaults.StylePresetId
            };

            await _historyRepository.SaveAsync(historyItem);
            _readinessService?.ReportExecutionOutcome(true);
            SetStatus($"Fiche « {document.Metadata.Title} » générée en {result.Elapsed.TotalSeconds:F1} s.", StatusSeverity.Success);
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

    /// <summary>Applique un modèle de démarrage rapide au champ « Sujet ».</summary>
    [RelayCommand]
    public void ApplyTopicTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return;
        Topic = template;
        SetStatus($"Modèle de sujet appliqué : « {template} ».", StatusSeverity.Info);
    }

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
            // La suppression du brouillon ne doit jamais interrompre l'utilisateur.
        }
    }

    /// <summary>Réinitialise les champs éditables du formulaire.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    public void ResetForm()
    {
        Topic = string.Empty;
        AdditionalInstructions = string.Empty;
        DurationMinutes = 60;
        UsePedagogicalGuide = true;
        ClearErrors();
        SetStatus("Formulaire réinitialisé.", StatusSeverity.Info);
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
                [nameof(DurationMinutes)] = DurationMinutes.ToString(),
                [nameof(AdditionalInstructions)] = AdditionalInstructions,
                [nameof(UsePedagogicalGuide)] = UsePedagogicalGuide ? "true" : "false"
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
                    if (fields.TryGetValue(nameof(DurationMinutes), out var du) && int.TryParse(du, out var minutes) && minutes is >= 5 and <= 240) DurationMinutes = minutes;
                    if (fields.TryGetValue(nameof(AdditionalInstructions), out var ai) && ai is not null) AdditionalInstructions = ai;
                    if (fields.TryGetValue(nameof(UsePedagogicalGuide), out var ug) && bool.TryParse(ug, out var guide)) UsePedagogicalGuide = guide;
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
            // Restauration silencieuse : le formulaire reste fonctionnel sans brouillon.
        }
    }

    // ------------------------------------------------------------------
    // Purge de rétention de l'historique (arrière-plan, unique à cet onglet)
    // ------------------------------------------------------------------

    private async Task RunRetentionSweepSafelyAsync()
    {
        if (_historyRetentionService is null) return;
        try
        {
            var removed = await _historyRetentionService.SweepAsync();
            if (removed <= 0) return;

            void Notify() => RetentionNotice = removed == 1
                ? "1 élément ancien purgé de l'historique."
                : $"{removed} éléments anciens purgés de l'historique.";

            if (_dispatcherQueue is not null) _dispatcherQueue.TryEnqueue(Notify);
            else Notify();
        }
        catch
        {
            // Maintenance silencieuse : ne jamais perturber l'enseignant.
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

    private static string GetPlainText(GeneratedDocument doc) => doc.ToPlainText();
}
