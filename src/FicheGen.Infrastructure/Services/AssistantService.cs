using System.Text.Json;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Ai;
using FicheGen.Core.Documents;
using FicheGen.Core.Prompts;

namespace FicheGen.Infrastructure.Services;

public sealed class AssistantService : IAssistantService
{
    private readonly ILlmClient? _llmClient;
    private readonly List<GeneratedDocument> _undoStack = new();
    private const int MaxUndoDepth = 10;

    public AssistantService(ILlmClient? llmClient = null)
    {
        _llmClient = llmClient;
    }

    public bool CanUndo => _undoStack.Count > 0;

    public void PushUndo(GeneratedDocument doc)
    {
        if (doc == null) return;
        _undoStack.Add(doc);
        if (_undoStack.Count > MaxUndoDepth)
        {
            _undoStack.RemoveAt(0); // keep last 10
        }
    }

    public GeneratedDocument? PopUndo()
    {
        if (_undoStack.Count == 0) return null;
        var last = _undoStack[_undoStack.Count - 1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        return last;
    }

    public async Task<AssistantIntentResult> ClassifyIntentAsync(string userMessage, AiRequestConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return new AssistantIntentResult(IntentKind.Question, 1.0, "Message vide.");

        if (_llmClient != null)
        {
            try
            {
                var req = PromptBuilder.BuildIntentPrompt(userMessage);
                var json = await _llmClient.GenerateAsync(req, config, ct).ConfigureAwait(false);
                var cleaned = JsonCleaner.Clean(json);

                using var doc = JsonDocument.Parse(cleaned);
                if (doc.RootElement.TryGetProperty("intent", out var intentProp))
                {
                    var intentStr = intentProp.GetString()?.ToLowerInvariant();
                    var confidence = doc.RootElement.TryGetProperty("confidence", out var confProp) ? confProp.GetDouble() : 0.8;

                    var kind = intentStr switch
                    {
                        "generer" => IntentKind.Generer,
                        "modifier" => IntentKind.Modifier,
                        "question" => IntentKind.Question,
                        _ => ClassifyIntentHeuristic(userMessage)
                    };

                    return new AssistantIntentResult(kind, confidence, "Classification AI.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fallback heuristic scoring
            }
        }

        var fallbackKind = ClassifyIntentHeuristic(userMessage);
        return new AssistantIntentResult(fallbackKind, 0.7, "Classification heuristique.");
    }

    public IAsyncEnumerable<string> StreamEditAsync(GeneratedDocument currentDoc, string editInstruction, AiRequestConfig config, CancellationToken ct)
    {
        if (_llmClient == null)
        {
            throw new InvalidOperationException("Le service Assistant requiert un ILlmClient actif pour les modifications.");
        }

        var docText = currentDoc.ToPlainText();
        var req = PromptBuilder.BuildEditPrompt(docText, editInstruction);
        return _llmClient.GenerateStreamAsync(req, config, ct);
    }

    public Task<string> AskQuestionAsync(GeneratedDocument currentDoc, string question, AiRequestConfig config, CancellationToken ct)
    {
        if (_llmClient == null)
        {
            throw new InvalidOperationException("Le service Assistant requiert un ILlmClient actif pour les questions.");
        }

        var docText = currentDoc.ToPlainText();
        var req = PromptBuilder.BuildQuestionPrompt(docText, question);
        return _llmClient.GenerateAsync(req, config, ct);
    }

    public IAsyncEnumerable<string> StreamQuestionAsync(
        GeneratedDocument currentDoc,
        string question,
        IReadOnlyList<(string Role, string Content)>? conversationHistory,
        AiRequestConfig config,
        CancellationToken ct)
    {
        if (_llmClient == null)
        {
            throw new InvalidOperationException("Le service Assistant requiert un ILlmClient actif pour les questions.");
        }

        var docText = currentDoc.ToPlainText();
        var req = PromptBuilder.BuildQuestionPrompt(docText, question, conversationHistory);
        return _llmClient.GenerateStreamAsync(req, config, ct);
    }

    public static IntentKind ClassifyIntentHeuristic(string message)
    {
        var msg = message.ToLowerInvariant();

        string[] editKeywords = { "modifie", "remplace", "ajoute", "corrige", "traduis", "enleve", "enlève", "change", "adapte", "reécris", "réécris" };
        string[] genKeywords = { "créer", "creer", "génère", "genere", "nouvelle", "nouveau" };

        foreach (var kw in editKeywords)
        {
            if (msg.Contains(kw)) return IntentKind.Modifier;
        }

        foreach (var kw in genKeywords)
        {
            if (msg.Contains(kw)) return IntentKind.Generer;
        }

        return IntentKind.Question;
    }
}
