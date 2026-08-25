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
        private readonly string _responseToReturn;

        public MockLlmClient(string responseToReturn)
        {
            _responseToReturn = responseToReturn;
        }

        public Task<string> GenerateAsync(LlmRequest req, AiRequestConfig cfg, CancellationToken ct)
        {
            return Task.FromResult(_responseToReturn);
        }

        public async IAsyncEnumerable<string> GenerateStreamAsync(LlmRequest req, AiRequestConfig cfg, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return _responseToReturn;
            await Task.CompletedTask;
        }
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
