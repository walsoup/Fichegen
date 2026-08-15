// ============================================================================
//  FicheGen.E2E.Tests — TestEnvironmentFactory
//  Fabrique d'environnement de test isolé avec SQLite temporaire,
//  bouchons LLM déterministes et ViewModels configurés.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.App.Services;
using FicheGen.App.ViewModels;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Ai;
using FicheGen.Core.Documents;
using FicheGen.Core.Services;
using FicheGen.Core.Storage;
using FicheGen.Infrastructure.Security;
using FicheGen.Infrastructure.Services;
using FicheGen.Infrastructure.Storage;

namespace FicheGen.E2E.Tests.Infrastructure;

/// <summary>
/// Implémentation de test de ILlmClient pour les tests E2E.
/// Intercepte les demandes de génération et d'assistant et produit des réponses valides.
/// </summary>
public sealed class TestLlmClient : ILlmClient, IAssistantService
{
    public bool SimulateDelay { get; set; } = false;
    public bool ShouldFail { get; set; } = false;
    public int DelayMs { get; set; } = 100;

    public async Task<string> GenerateAsync(LlmRequest request, AiRequestConfig config, CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("Erreur simulée du service LLM.");
        }

        if (SimulateDelay)
        {
            await Task.Delay(DelayMs, cancellationToken);
        }

        var classLevel = "CM2";
        if (request.UserPrompt.Contains("6e")) classLevel = "6e";
        else if (request.UserPrompt.Contains("CM1")) classLevel = "CM1";
        else if (request.UserPrompt.Contains("CE2")) classLevel = "CE2";

        var subject = "Mathématiques";
        if (request.UserPrompt.Contains("Français")) subject = "Français";
        else if (request.UserPrompt.Contains("Histoire")) subject = "Histoire";
        else if (request.UserPrompt.Contains("Sciences")) subject = "Sciences";

        // Construire un JSON valide selon GeneratedDocument
        var sampleJson = $$"""
        {
          "metadata": {
            "title": "Fiche de test pédagogique",
            "subtitle": "Sous-titre de test",
            "classLevel": "{{classLevel}}",
            "subject": "{{subject}}",
            "durationMinutes": 60,
            "themeColor": "#2B579A",
            "stylePresetId": "modern"
          },
          "blocks": [
            {
              "$type": "heading",
              "id": "h1",
              "level": 1,
              "runs": [{ "text": "Objectifs de la leçon", "isBold": true }]
            },
            {
              "$type": "paragraph",
              "id": "p1",
              "runs": [{ "text": "Comprendre les principes fondamentaux du sujet étudié.", "isItalic": false }]
            }
          ]
        }
        """;

        return sampleJson;
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        LlmRequest request,
        AiRequestConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("Erreur de flux simulée.");
        }

        var json = await GenerateAsync(request, config, cancellationToken);
        var chunkSize = 20;

        for (int i = 0; i < json.Length; i += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SimulateDelay)
            {
                await Task.Delay(20, cancellationToken);
            }
            yield return json.Substring(i, Math.Min(chunkSize, json.Length - i));
        }
    }

    public async IAsyncEnumerable<string> StreamEditAsync(
        GeneratedDocument document,
        string userPrompt,
        AiRequestConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (ShouldFail) throw new InvalidOperationException("Erreur de modification assistée.");

        var response = $"<p>Modification appliquée pour : {userPrompt}</p>";
        for (int i = 0; i < response.Length; i += 5)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SimulateDelay) await Task.Delay(10, cancellationToken);
            yield return response.Substring(i, Math.Min(5, response.Length - i));
        }
    }

    public async Task<string> AskQuestionAsync(
        GeneratedDocument document,
        string question,
        AiRequestConfig config,
        CancellationToken cancellationToken = default)
    {
        if (ShouldFail) throw new InvalidOperationException("Erreur lors de la question.");
        if (SimulateDelay) await Task.Delay(DelayMs, cancellationToken);
        return $"Réponse à la question « {question} » basée sur le document « {document.Metadata.Title} ».";
    }

    private readonly Stack<GeneratedDocument> _undoStack = new();

    public Task<AssistantIntentResult> ClassifyIntentAsync(string userMessage, AiRequestConfig config, CancellationToken ct)
    {
        return Task.FromResult(new AssistantIntentResult(IntentKind.Question, 0.9, "Intention de test"));
    }

    public void PushUndo(GeneratedDocument doc) => _undoStack.Push(doc);
    public bool CanUndo => _undoStack.Count > 0;
    public GeneratedDocument? PopUndo() => _undoStack.Count > 0 ? _undoStack.Pop() : null;
}

/// <summary>
/// Conteneur des composants d'environnement de test E2E.
/// </summary>
public sealed class TestEnvironment : IDisposable
{
    public string TempFolder { get; }
    public string DbPath { get; }
    public string SettingsPath { get; }
    public TestLlmClient LlmClient { get; }
    public HistoryRepository HistoryRepository { get; }
    public SettingsStore SettingsStore { get; }
    public StylePresetService StylePresetService { get; }
    public GenerationOrchestrator Orchestrator { get; }
    public ResultViewModel ResultViewModel { get; }
    public FicheFormViewModel FicheFormViewModel { get; }
    public EvaluationViewModel EvaluationViewModel { get; }
    public QuizViewModel QuizViewModel { get; }
    public HistoryViewModel HistoryViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public AssistantViewModel AssistantViewModel { get; }

    public TestEnvironment(
        string tempFolder,
        string dbPath,
        string settingsPath,
        TestLlmClient llmClient,
        HistoryRepository historyRepository,
        SettingsStore settingsStore,
        StylePresetService stylePresetService,
        GenerationOrchestrator orchestrator,
        ResultViewModel resultViewModel,
        FicheFormViewModel ficheFormViewModel,
        EvaluationViewModel evaluationViewModel,
        QuizViewModel quizViewModel,
        HistoryViewModel historyViewModel,
        SettingsViewModel settingsViewModel,
        AssistantViewModel assistantViewModel)
    {
        TempFolder = tempFolder;
        DbPath = dbPath;
        SettingsPath = settingsPath;
        LlmClient = llmClient;
        HistoryRepository = historyRepository;
        SettingsStore = settingsStore;
        StylePresetService = stylePresetService;
        Orchestrator = orchestrator;
        ResultViewModel = resultViewModel;
        FicheFormViewModel = ficheFormViewModel;
        EvaluationViewModel = evaluationViewModel;
        QuizViewModel = quizViewModel;
        HistoryViewModel = historyViewModel;
        SettingsViewModel = settingsViewModel;
        AssistantViewModel = assistantViewModel;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TempFolder))
            {
                Directory.Delete(TempFolder, recursive: true);
            }
        }
        catch
        {
            // Nettoyage temporaire en fin de test
        }
    }
}

/// <summary>
/// Fabrique statique pour instancier des environnements de test E2E isolés.
/// </summary>
public static class TestEnvironmentFactory
{
    public static TestEnvironment Create()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "FicheGenE2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        var dbPath = Path.Combine(tempFolder, "history_test.db");
        var settingsPath = Path.Combine(tempFolder, "settings_test.json");

        var llmClient = new TestLlmClient();
        var historyRepo = new HistoryRepository(dbPath);
        historyRepo.InitializeDatabase();

        var credentialStore = new DpapiCredentialStore();
        var settingsStore = new SettingsStore(credentialStore, settingsPath);
        var stylePresetService = new StylePresetService();
        var orchestrator = new GenerationOrchestrator(llmClient, guideService: null);

        var resultVm = new ResultViewModel(stylePresetService: stylePresetService);
        var ficheVm = new FicheFormViewModel(orchestrator, settingsStore, historyRepo, stylePresetService, resultVm);
        var evalVm = new EvaluationViewModel(orchestrator, settingsStore, historyRepo, stylePresetService, resultVm);
        var quizVm = new QuizViewModel(orchestrator, settingsStore, historyRepo, stylePresetService, resultVm);
        var historyVm = new HistoryViewModel(historyRepo, resultVm, stylePresetService);
        var pickerService = new FicheGen.App.Services.PickerService();
        var settingsVm = new SettingsViewModel(settingsStore, credentialStore, pickerService, stylePresetService);
        var assistantVm = new AssistantViewModel(llmClient, settingsStore, resultVm);

        return new TestEnvironment(
            tempFolder,
            dbPath,
            settingsPath,
            llmClient,
            historyRepo,
            settingsStore,
            stylePresetService,
            orchestrator,
            resultVm,
            ficheVm,
            evalVm,
            quizVm,
            historyVm,
            settingsVm,
            assistantVm
        );
    }
}
