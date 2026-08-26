using System.Diagnostics;
using System.Text;
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

    public async Task<GenerationResult> GenerateFicheStreamingAsync(
        FicheParameters parameters,
        AiRequestConfig config,
        string? guidesDir = null,
        IProgress<string>? chunkProgress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string? lessonContext = null;

        if (parameters.UsePedagogicalGuide && _guideService != null && !string.IsNullOrWhiteSpace(guidesDir))
        {
            lessonContext = await ResolveGuideContextAsync(parameters.ClassLevel, parameters.Topic, guidesDir, _guideService, ct).ConfigureAwait(false);
        }

        var req = PromptBuilder.BuildFichePrompt(parameters, lessonContext);
        return await ProcessStreamingGenerationAsync(req, config, parameters.Topic, lessonContext, sw, chunkProgress, ct).ConfigureAwait(false);
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
        var normalizedDoc = NormalizeBaremeIfNeeded(result.Document, parameters.TargetPoints);
        return ReferenceEquals(normalizedDoc, result.Document) ? result : result with { Document = normalizedDoc };
    }

    public async Task<GenerationResult> GenerateEvaluationStreamingAsync(
        EvalParameters parameters,
        AiRequestConfig config,
        string? guidesDir = null,
        IProgress<string>? chunkProgress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string? lessonContext = null;

        if (parameters.UsePedagogicalGuide && _guideService != null && !string.IsNullOrWhiteSpace(guidesDir))
        {
            lessonContext = await ResolveGuideContextAsync(parameters.ClassLevel, parameters.Topic, guidesDir, _guideService, ct).ConfigureAwait(false);
        }

        var req = PromptBuilder.BuildEvalPrompt(parameters, lessonContext);
        var result = await ProcessStreamingGenerationAsync(req, config, parameters.Topic, lessonContext, sw, chunkProgress, ct).ConfigureAwait(false);

        var normalizedDoc = NormalizeBaremeIfNeeded(result.Document, parameters.TargetPoints);
        return ReferenceEquals(normalizedDoc, result.Document) ? result : result with { Document = normalizedDoc };
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

    public async Task<GenerationResult> GenerateQuizStreamingAsync(
        QuizParameters parameters,
        AiRequestConfig config,
        IProgress<string>? chunkProgress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var req = PromptBuilder.BuildQuizPrompt(parameters, lessonContext: null);
        return await ProcessStreamingGenerationAsync(req, config, parameters.Topic, lessonContext: null, sw, chunkProgress, ct).ConfigureAwait(false);
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

    private async Task<GenerationResult> ProcessStreamingGenerationAsync(
        LlmRequest req,
        AiRequestConfig config,
        string fallbackTitle,
        string? lessonContext,
        Stopwatch sw,
        IProgress<string>? chunkProgress,
        CancellationToken ct)
    {
        var sb = new StringBuilder();

        await foreach (var chunk in _llmClient.GenerateStreamAsync(req, config, ct).ConfigureAwait(false))
        {
            sb.Append(chunk);
            chunkProgress?.Report(chunk);
        }

        var rawResponse = sb.ToString();

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Guide resolution is best-effort enrichment: generation continues
            // without lesson context when the guide cannot be read.
            return null;
        }
    }

    /// <summary>
    /// Scans evaluation tables for a barème/points column and appends a warning
    /// callout when the points do not sum to the expected total. Returns the
    /// original document instance when no warning is needed.
    /// </summary>
    internal static GeneratedDocument NormalizeBaremeIfNeeded(GeneratedDocument doc, double targetTotal = 20.0)
    {
        const double tolerance = 0.01;

        foreach (var block in doc.Blocks)
        {
            if (block is not TableBlock table || table.Headers.Count == 0)
                continue;

            for (var col = 0; col < table.Headers.Count; col++)
            {
                var header = NormalizeAccents(table.Headers[col]).ToLowerInvariant();
                var isBaremeColumn = header.Contains("bareme") || header.Contains("point") || header.Contains("note");
                if (!isBaremeColumn)
                    continue;

                double sum = 0;
                var hasValues = false;
                foreach (var row in table.Rows)
                {
                    if (col >= row.Count) continue;
                    if (TryParseFrenchDouble(row[col], out var value))
                    {
                        sum += value;
                        hasValues = true;
                    }
                }

                if (hasValues && Math.Abs(sum - targetTotal) > tolerance)
                {
                    var warning = new CalloutBoxBlock("warning", new List<Block>
                    {
                        new ParagraphBlock(
                            $"Attention : le total du barème est de {FormatFrenchDouble(sum)} points au lieu de {FormatFrenchDouble(targetTotal)}. Ajustez la répartition des points.")
                    });
                    return doc with { Blocks = doc.Blocks.Append(warning).ToList() };
                }
            }
        }

        return doc;
    }

    private static string NormalizeAccents(string input) =>
        input.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Aggregate(new StringBuilder(), (sb, c) => sb.Append(c))
            .ToString();

    private static bool TryParseFrenchDouble(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var cleaned = text.Trim().Replace(',', '.').TrimEnd('.', 'p', 't', 's', ' ', '/');
        return double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string FormatFrenchDouble(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

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
