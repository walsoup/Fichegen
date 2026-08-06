namespace FicheGen.Core.Ai;

public sealed record LlmRequest(
    string Purpose,                 // "fiche" | "eval" | "quiz" | "toc" | "offset" | "syntax" | "chat" | "intent" | "style"
    string SystemPrompt,
    string UserPrompt,
    double Temperature,
    bool ResponseJson,
    string? ModelOverride = null)
{
}

