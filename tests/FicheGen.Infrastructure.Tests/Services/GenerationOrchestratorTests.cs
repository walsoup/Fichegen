using System.Runtime.CompilerServices;
using FicheGen.Core.Ai;
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
}
