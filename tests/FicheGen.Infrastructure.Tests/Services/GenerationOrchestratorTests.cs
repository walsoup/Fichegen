using System.Runtime.CompilerServices;
using FicheGen.Core.Ai;
using FicheGen.Core.Documents;
using FicheGen.Core.Prompts;
using FicheGen.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests.Services;

public class GenerationOrchestratorTests
{
    private class MockLlmClient : ILlmClient
    {
        private readonly Queue<string> _responsesToReturn;

        public List<LlmRequest> ReceivedRequests { get; } = new();

        public MockLlmClient(params string[] responsesToReturn)
        {
            _responsesToReturn = new Queue<string>(responsesToReturn);
        }

        private string NextResponse()
            => _responsesToReturn.Count > 1 ? _responsesToReturn.Dequeue() : _responsesToReturn.Peek();

        public Task<string> GenerateAsync(LlmRequest req, AiRequestConfig cfg, CancellationToken ct)
        {
            ReceivedRequests.Add(req);
            return Task.FromResult(NextResponse());
        }

        public async IAsyncEnumerable<string> GenerateStreamAsync(LlmRequest req, AiRequestConfig cfg, [EnumeratorCancellation] CancellationToken ct)
        {
            ReceivedRequests.Add(req);
            yield return NextResponse();
            await Task.CompletedTask;
        }
    }

    private static string BuildEvaluationJson(params (string Title, string Points)[] exercises)
    {
        var blocks = string.Join(",\n", exercises.Select(e => $@"{{ ""$type"": ""heading"", ""level"": 1, ""runs"": [{{ ""text"": ""{e.Title} ({e.Points} points)"" }}] }}"));
        return $@"{{
          ""metadata"": {{ ""title"": ""Évaluation Fractions"", ""classLevel"": ""CM2"", ""docType"": ""evaluation"" }},
          ""blocks"": [
            {blocks}
          ]
        }}";
    }

    [Fact]
    public async Task GenerateEvaluationAsync_TooFewExercisesForExhaustif_RequestsExpansionAndReturnsExpandedDocument()
    {
        var shortDoc = BuildEvaluationJson(("Exercice 1", "20"));
        var expandedDoc = BuildEvaluationJson(("Exercice 1", "6"), ("Exercice 2", "5"), ("Exercice 3", "4"), ("Exercice 4", "3"), ("Exercice 5", "1"), ("Exercice 6", "1"));
        var mockLlm = new MockLlmClient(shortDoc, expandedDoc);
        var orchestrator = new GenerationOrchestrator(mockLlm);

        var result = await orchestrator.GenerateEvaluationAsync(
            new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20, DocumentLength: "Exhaustif"),
            CreateConfig(),
            ct: CancellationToken.None,
            enablePipeline: false);

        mockLlm.ReceivedRequests.Should().HaveCount(2);
        mockLlm.ReceivedRequests[1].UserPrompt.Should().Contain("Conserve intégralement");
        EvaluationSpec.CountExercises(result.Document).Should().Be(6);
        result.PreviewHtml.Should().Contain("Exercice 6");
        result.IsFallback.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateEvaluationAsync_EnoughExercises_MakesSingleLlmCall()
    {
        var fullDoc = BuildEvaluationJson(
            ("Exercice 1", "4"), ("Exercice 2", "4"), ("Exercice 3", "4"),
            ("Exercice 4", "4"), ("Exercice 5", "2"), ("Exercice 6", "2"));
        var mockLlm = new MockLlmClient(fullDoc);
        var orchestrator = new GenerationOrchestrator(mockLlm);

        var result = await orchestrator.GenerateEvaluationAsync(
            new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20, DocumentLength: "Exhaustif"),
            CreateConfig(),
            ct: CancellationToken.None,
            enablePipeline: false);

        mockLlm.ReceivedRequests.Should().HaveCount(1);
        EvaluationSpec.CountExercises(result.Document).Should().Be(6);
    }

    [Fact]
    public async Task GenerateEvaluationAsync_ExpansionReturnsInvalidJson_KeepsOriginalDocument()
    {
        var shortDoc = BuildEvaluationJson(("Exercice 1", "20"));
        var mockLlm = new MockLlmClient(shortDoc, "Ceci n'est pas du JSON valide.");
        var orchestrator = new GenerationOrchestrator(mockLlm);

        var result = await orchestrator.GenerateEvaluationAsync(
            new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20, DocumentLength: "Exhaustif"),
            CreateConfig(),
            ct: CancellationToken.None,
            enablePipeline: false);

        mockLlm.ReceivedRequests.Should().HaveCount(2);
        EvaluationSpec.CountExercises(result.Document).Should().Be(1);
        result.Document.Metadata.Title.Should().Be("Évaluation Fractions");
    }

    [Fact]
    public async Task GenerateEvaluationAsync_ExpansionReturnsFewerThanTarget_KeepsPartialImprovement()
    {
        var shortDoc = BuildEvaluationJson(("Exercice 1", "20"));
        var improvedDoc = BuildEvaluationJson(("Exercice 1", "18"), ("Exercice 2", "2"));
        var mockLlm = new MockLlmClient(shortDoc, improvedDoc);
        var orchestrator = new GenerationOrchestrator(mockLlm);

        var result = await orchestrator.GenerateEvaluationAsync(
            new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20, DocumentLength: "Exhaustif"),
            CreateConfig(),
            ct: CancellationToken.None,
            enablePipeline: false);

        // The expansion didn't reach the target but still adds exercises:
        // the improvement is kept rather than discarded.
        EvaluationSpec.CountExercises(result.Document).Should().Be(2);
    }

    [Fact]
    public async Task GenerateEvaluationAsync_ExpansionReturnsSameExerciseCount_KeepsOriginalDocument()
    {
        var shortDoc = BuildEvaluationJson(("Exercice 1", "20"));
        var mockLlm = new MockLlmClient(shortDoc);
        var orchestrator = new GenerationOrchestrator(mockLlm);

        var result = await orchestrator.GenerateEvaluationAsync(
            new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20, DocumentLength: "Exhaustif"),
            CreateConfig(),
            ct: CancellationToken.None,
            enablePipeline: false);

        // The (single-response) mock returns the same document for the
        // expansion call, which adds nothing: original is kept.
        EvaluationSpec.CountExercises(result.Document).Should().Be(1);
        result.IsFallback.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateEvaluationStreamingAsync_TooFewExercises_RequestsExpansion()
    {
        var shortDoc = BuildEvaluationJson(("Exercice 1", "20"));
        var expandedDoc = BuildEvaluationJson(
            ("Exercice 1", "4"), ("Exercice 2", "4"), ("Exercice 3", "3"),
            ("Exercice 4", "3"), ("Exercice 5", "3"), ("Exercice 6", "3"));
        var mockLlm = new MockLlmClient(shortDoc, expandedDoc);
        var orchestrator = new GenerationOrchestrator(mockLlm);

        var result = await orchestrator.GenerateEvaluationStreamingAsync(
            new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20, DocumentLength: "Exhaustif"),
            CreateConfig(),
            ct: CancellationToken.None,
            enablePipeline: false);

        mockLlm.ReceivedRequests.Should().HaveCount(2);
        EvaluationSpec.CountExercises(result.Document).Should().Be(6);
    }

    [Fact]
    public async Task GenerateEvaluationAsync_PipelineSuccess_GeneratesPlanAndExercises()
    {
        var planJson = @"{
          ""title"": ""Évaluation Fractions"",
          ""exercises"": [
            { ""title"": ""Exercice 1"", ""points"": 10, ""competences"": ""Fractions simples"" },
            { ""title"": ""Exercice 2"", ""points"": 10, ""competences"": ""Calculs"" }
          ]
        }";

        var ex1Draft = @"{
          ""consigne"": ""Consigne 1"",
          ""questions"": [""a) Question 1""],
          ""corrige"": [""a) Réponse 1 (10 pts)""]
        }";

        var ex2Draft = @"{
          ""consigne"": ""Consigne 2"",
          ""questions"": [""a) Question 2""],
          ""corrige"": [""a) Réponse 2 (10 pts)""]
        }";

        var mockLlm = new MockLlmClient(planJson, ex1Draft, ex2Draft);
        var orchestrator = new GenerationOrchestrator(mockLlm);

        var result = await orchestrator.GenerateEvaluationAsync(
            new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20, DocumentLength: "Bref"),
            CreateConfig(),
            ct: CancellationToken.None,
            enablePipeline: true);

        mockLlm.ReceivedRequests.Should().HaveCount(3);
        EvaluationSpec.CountExercises(result.Document).Should().Be(2);
        result.IsFallback.Should().BeFalse();
        result.PreviewHtml.Should().Contain("Exercice 1");
        result.PreviewHtml.Should().Contain("Exercice 2");
    }

    [Fact]
    public async Task GenerateEvaluationAsync_PipelineFails_FallsBackToSingleShot()
    {
        var singleShotDoc = BuildEvaluationJson(("Exercice 1", "10"), ("Exercice 2", "10"));
        var mockLlm = new MockLlmClient("Invalid Plan JSON", singleShotDoc);
        var orchestrator = new GenerationOrchestrator(mockLlm);

        var result = await orchestrator.GenerateEvaluationAsync(
            new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20, DocumentLength: "Bref"),
            CreateConfig(),
            ct: CancellationToken.None,
            enablePipeline: true);

        mockLlm.ReceivedRequests.Should().HaveCount(2);
        EvaluationSpec.CountExercises(result.Document).Should().Be(2);
        result.IsFallback.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateFicheAsync_ValidJsonResponse_ProducesResultWithHtml()
    {
        var mockJson = @"{
          ""metadata"": { ""title"": ""Séquence Vocabulaire"", ""classLevel"": ""CE2"" },
          ""blocks"": [
            { ""$type"": ""heading"", ""level"": 1, ""runs"": [{ ""text"": ""Les synonymes"" }] }
          ]
        }";

        var mockLlm = new MockLlmClient(mockJson);
        var orchestrator = new GenerationOrchestrator(mockLlm);

        var config = new AiRequestConfig(
            GlobalProvider: "aistudio",
            DefaultModels: new Dictionary<string, string>(),
            RoutingOverrides: new Dictionary<string, RoutingOverride>(),
            ProxyBaseUrl: "http://localhost:11434",
            VertexProject: "",
            VertexRegion: "",
            Temperatures: new Dictionary<string, double>(),
            SecretResolver: (k, ct) => ValueTask.FromResult<string?>("key")
        );

        var result = await orchestrator.GenerateFicheAsync(
            new FicheParameters("CE2", "Français", "Les synonymes"),
            config,
            ct: CancellationToken.None
        );

        result.Should().NotBeNull();
        result.IsFallback.Should().BeFalse();
        result.Document.Metadata.Title.Should().Be("Séquence Vocabulaire");
        result.PreviewHtml.Should().Contain("Séquence Vocabulaire");
        result.PreviewHtml.Should().Contain("Les synonymes");
        result.RawResponse.Should().Be(mockJson);
        result.RawPrompt.Should().NotBeNullOrWhiteSpace();
        result.Document.SourceJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerateFicheAsync_InvalidJson_UsesFallbackMarkdownRenderer()
    {
        var mockMarkdown = @"# Titre de secours
Ceci est du markdown brut renvoyé par l'IA au lieu du JSON.";

        var mockLlm = new MockLlmClient(mockMarkdown);
        var orchestrator = new GenerationOrchestrator(mockLlm);

        var config = new AiRequestConfig(
            GlobalProvider: "aistudio",
            DefaultModels: new Dictionary<string, string>(),
            RoutingOverrides: new Dictionary<string, RoutingOverride>(),
            ProxyBaseUrl: "http://localhost:11434",
            VertexProject: "",
            VertexRegion: "",
            Temperatures: new Dictionary<string, double>(),
            SecretResolver: (k, ct) => ValueTask.FromResult<string?>("key")
        );

        var result = await orchestrator.GenerateFicheAsync(
            new FicheParameters("CE2", "Français", "Les synonymes"),
            config,
            ct: CancellationToken.None
        );

        result.Should().NotBeNull();
        result.IsFallback.Should().BeTrue();
        result.WarningMessage.Should().NotBeNull();
        result.PreviewHtml.Should().Contain("Mode dégradé");
        result.PreviewHtml.Should().Contain("Titre de secours");
    }

    [Fact]
    public async Task GenerateEvaluationAsync_BaremeSumMismatch_AppendsWarningCallout()
    {
        var mockJson = @"{
          ""metadata"": { ""title"": ""Évaluation Fractions"", ""classLevel"": ""CM2"" },
          ""blocks"": [
            { ""$type"": ""table"",
              ""headers"": [""Exercice"", ""Barème""],
              ""rows"": [
                [""Ex. 1"", ""8""],
                [""Ex. 2"", ""7""]
              ]
            }
          ]
        }";

        var orchestrator = new GenerationOrchestrator(new MockLlmClient(mockJson));
        var config = CreateConfig();

        var result = await orchestrator.GenerateEvaluationAsync(
            new EvalParameters("CM2", "Mathématiques", "Fractions"),
            config,
            ct: CancellationToken.None);

        result.Document.Blocks.Should().HaveCount(2);
        var warning = result.Document.Blocks[1].Should().BeOfType<CalloutBoxBlock>().Subject;
        warning.Kind.Should().Be("warning");
    }

    [Fact]
    public async Task GenerateEvaluationAsync_BaremeSumCorrect_DoesNotAppendWarning()
    {
        var mockJson = @"{
          ""metadata"": { ""title"": ""Évaluation Fractions"", ""classLevel"": ""CM2"" },
          ""blocks"": [
            { ""$type"": ""table"",
              ""headers"": [""Exercice"", ""Points""],
              ""rows"": [
                [""Ex. 1"", ""12""],
                [""Ex. 2"", ""8""]
              ]
            }
          ]
        }";

        var orchestrator = new GenerationOrchestrator(new MockLlmClient(mockJson));
        var config = CreateConfig();

        var result = await orchestrator.GenerateEvaluationAsync(
            new EvalParameters("CM2", "Mathématiques", "Fractions", TargetPoints: 20),
            config,
            ct: CancellationToken.None);

        result.Document.Blocks.Should().HaveCount(1);
    }

    private static AiRequestConfig CreateConfig() => new(
        GlobalProvider: "aistudio",
        DefaultModels: new Dictionary<string, string>(),
        RoutingOverrides: new Dictionary<string, RoutingOverride>(),
        ProxyBaseUrl: "http://localhost:11434",
        VertexProject: "",
        VertexRegion: "",
        Temperatures: new Dictionary<string, double>(),
        SecretResolver: (k, ct) => ValueTask.FromResult<string?>("key")
    );

    // Note: Live external API integration tests should run in a dedicated test suite with environment variables, not in standard CI.
}
