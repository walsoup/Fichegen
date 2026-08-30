using FicheGen.Core.Documents;

namespace FicheGen.Core.Services;

public sealed record GenerationResult(
    GeneratedDocument Document,
    string PreviewHtml,
    string RawResponse,
    TimeSpan Elapsed,
    string? LessonContextUsed = null,
    bool IsFallback = false,
    string? WarningMessage = null,
    string? RawPrompt = null)
{
}

