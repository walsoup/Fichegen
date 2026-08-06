using System.Diagnostics;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Ai;
using FicheGen.Core.Documents;
using FicheGen.Core.Prompts;
using FicheGen.Core.Services;

namespace FicheGen.Infrastructure.Services;

public sealed class GenerationOrchestrator
{
    private readonly ILlmClient _llmClient;
    private readonly IPdfGuideService? _guideService;

    public GenerationOrchestrator(ILlmClient llmClient, IPdfGuideService? guideService = null)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _guideService = guideService;
    }

    public async Task<GenerationResult> GenerateFicheAsync(
        FicheParameters parameters,
        AiRequestConfig config,
        string? guidesDir = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string? lessonContext = null;

        if (parameters.UsePedagogicalGuide && _guideService != null && !string.IsNullOrWhiteSpace(guidesDir))
        {
            lessonContext = await ResolveGuideContextAsync(parameters.ClassLevel, parameters.Topic, guidesDir, _guideService, ct).ConfigureAwait(false);
        }

        var req = PromptBuilder.BuildFichePrompt(parameters, lessonContext);
        return await ProcessGenerationAsync(req, config, parameters.Topic, lessonContext, sw, ct).ConfigureAwait(false);
    }

    public async Task<GenerationResult> GenerateEvaluationAsync(
        EvalParameters parameters,
        AiRequestConfig config,
        string? guidesDir = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string? lessonContext = null;

        if (parameters.UsePedagogicalGuide && _guideService != null && !string.IsNullOrWhiteSpace(guidesDir))
        {
            lessonContext = await ResolveGuideContextAsync(parameters.ClassLevel, parameters.Topic, guidesDir, _guideService, ct).ConfigureAwait(false);
        }

        var req = PromptBuilder.BuildEvalPrompt(parameters, lessonContext);
        var result = await ProcessGenerationAsync(req, config, parameters.Topic, lessonContext, sw, ct).ConfigureAwait(false);

        // Normalize barème if evaluation
        NormalizeBaremeIfNeeded(result.Document);

        return result;
    }

    public async Task<GenerationResult> GenerateQuizAsync(
        QuizParameters parameters,
        AiRequestConfig config,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var req = PromptBuilder.BuildQuizPrompt(parameters, lessonContext: null);
        return await ProcessGenerationAsync(req, config, parameters.Topic, lessonContext: null, sw, ct).ConfigureAwait(false);
    }

    private async Task<GenerationResult> ProcessGenerationAsync(
        LlmRequest req,
        AiRequestConfig config,
        string fallbackTitle,
        string? lessonContext,
        Stopwatch sw,
        CancellationToken ct)
    {
        var rawResponse = await _llmClient.GenerateAsync(req, config, ct).ConfigureAwait(false);

        bool isFallback = false;
        string? warningMessage = null;

        if (!JsonCleaner.TryDeserializeDocument(rawResponse, out var doc) || doc == null)
        {
            isFallback = true;
            warningMessage = "Format JSON invalide — conversion dégradée depuis le Markdown.";
            doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(rawResponse, fallbackTitle);
        }

        var html = HtmlRenderer.RenderToFullHtml(doc);
        sw.Stop();

        return new GenerationResult(
            Document: doc,
            PreviewHtml: html,
            RawResponse: rawResponse,
            Elapsed: sw.Elapsed,
            LessonContextUsed: lessonContext,
            IsFallback: isFallback,
            WarningMessage: warningMessage
        );
    }

    private static async Task<string?> ResolveGuideContextAsync(
        string classLevel,
        string topic,
        string guidesDir,
        IPdfGuideService guideService,
        CancellationToken ct)
    {
        try
        {
            var guideFile = guideService.FindGuideFile(classLevel, guidesDir);
            if (string.IsNullOrEmpty(guideFile))
                return null;

            var tocResult = await guideService.GetTocAsync(guideFile, ct).ConfigureAwait(false);
            if (tocResult == null || tocResult.Entries.Count == 0)
                return null;

            // Match best entry by title
            var bestMatch = tocResult.Entries
                .OrderBy(e => ComputeLevenshteinDistance(e.Title.ToLowerInvariant(), topic.ToLowerInvariant()))
                .FirstOrDefault();

            if (bestMatch == null)
                return null;

            var endPage = bestMatch.PhysicalPage + 3; // Extract 3 pages by default
            return await guideService.ExtractLessonTextAsync(guideFile, bestMatch.PhysicalPage, endPage, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static void NormalizeBaremeIfNeeded(GeneratedDocument doc)
    {
        // Check table or keyvalue grid for barème sum
        // If sum != 20, add warning note block
    }

    private static int ComputeLevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        var d = new int[s.Length + 1, t.Length + 1];

        for (var i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++) d[0, j] = j;

        for (var i = 1; i <= s.Length; i++)
        {
            for (var j = 1; j <= t.Length; j++)
            {
                var cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s.Length, t.Length];
    }
}
