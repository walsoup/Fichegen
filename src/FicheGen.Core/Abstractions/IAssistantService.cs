using FicheGen.Core.Ai;
using FicheGen.Core.Documents;

namespace FicheGen.Core.Abstractions;

public enum IntentKind
{
    Generer,
    Modifier,
    Question
}

public sealed record AssistantIntentResult(
    IntentKind Intent,
    double Confidence,
    string Reasoning);

public interface IAssistantService
{
    Task<AssistantIntentResult> ClassifyIntentAsync(string userMessage, AiRequestConfig config, CancellationToken ct);

    IAsyncEnumerable<string> StreamEditAsync(
        GeneratedDocument currentDoc,
        string editInstruction,
        AiRequestConfig config,
        CancellationToken ct);

    Task<string> AskQuestionAsync(
        GeneratedDocument currentDoc,
        string question,
        AiRequestConfig config,
        CancellationToken ct);

    void PushUndo(GeneratedDocument doc);
    bool CanUndo { get; }
    GeneratedDocument? PopUndo();
}
