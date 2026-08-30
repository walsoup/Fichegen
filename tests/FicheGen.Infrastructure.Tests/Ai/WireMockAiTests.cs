using FicheGen.Core.Ai;
using FicheGen.Infrastructure.Ai;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace FicheGen.Infrastructure.Tests.Ai;

public class WireMockAiTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly LlmClient _llmClient;

    public WireMockAiTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient();
        _llmClient = new LlmClient(_httpClient, new TestLlmRouter(_server.Url!));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Stop();
        _server.Dispose();
    }

    private AiRequestConfig CreateTestConfig(string globalProvider = "aistudio")
    {
        return new AiRequestConfig(
            GlobalProvider: globalProvider,
            DefaultModels: new Dictionary<string, string>
            {
                { "aistudio", "gemini-2.5-pro" },
                { "vertex", "gemini-2.5-pro" },
                { "proxy", "qwen2.5" },
                { "vercel", "default" }
            },
            RoutingOverrides: new Dictionary<string, RoutingOverride>(),
            ProxyBaseUrl: _server.Url!,
            VertexProject: "test-proj",
            VertexRegion: "europe-west1",
            Temperatures: new Dictionary<string, double> { { "fiche", 0.7 } },
            SecretResolver: (key, ct) => ValueTask.FromResult<string?>("test-secret-key")
        );
    }

    [Fact]
    public async Task Gemini_NonStreaming_ReturnsParsedText()
    {
        // Arrange
        _server
            .Given(Request.Create().WithPath("/v1beta/models/gemini-2.5-pro:generateContent").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(@"{
                    ""candidates"": [
                        {
                            ""content"": {
                                ""parts"": [{ ""text"": ""Bonjour de Gemini !"" }]
                            }
                        }
                    ]
                }"));

        var config = CreateTestConfig(globalProvider: "aistudio");
        var req = new LlmRequest("fiche", "System prompt", "User prompt", 0.7, true);

        // Act
        var result = await _llmClient.GenerateAsync(req, config, CancellationToken.None);

        // Assert
        result.Should().Be("Bonjour de Gemini !");
    }

    [Fact]
    public async Task OpenAiCompatible_Streaming_ReturnsChunks()
    {
        // Arrange
        _server
            .Given(Request.Create().WithPath("/chat/completions").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/event-stream")
                .WithBody("data: {\"choices\":[{\"delta\":{\"content\":\"Part 1 \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"Part 2\"}}]}\n\ndata: [DONE]\n\n"));

        var config = CreateTestConfig(globalProvider: "proxy");
        var req = new LlmRequest("fiche", "System", "User", 0.7, false);

        // Act
        var chunks = new List<string>();
        await foreach (var chunk in _llmClient.GenerateStreamAsync(req, config, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        string.Join("", chunks).Should().Be("Part 1 Part 2");
    }

    [Fact]
    public async Task Vercel_RawStream_ReturnsChunks()
    {
        // Arrange
        _server
            .Given(Request.Create().WithPath("/api/chat").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/plain")
                .WithBody("Raw stream chunk 1. Raw stream chunk 2."));

        var config = CreateTestConfig(globalProvider: "vercel");
        var req = new LlmRequest("fiche", "System", "User", 0.7, false);

        // Act
        var chunks = new List<string>();
        await foreach (var chunk in _llmClient.GenerateStreamAsync(req, config, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        string.Join("", chunks).Should().Be("Raw stream chunk 1. Raw stream chunk 2.");
    }

    [Fact]
    public async Task Error_401_ThrowsLlmException_Auth()
    {
        // Arrange
        _server
            .Given(Request.Create().WithPath("/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401).WithBody("Unauthorized"));

        var config = CreateTestConfig(globalProvider: "proxy");
        var req = new LlmRequest("fiche", "System", "User", 0.7, false);

        // Act
        Func<Task> act = async () => await _llmClient.GenerateAsync(req, config, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<LlmException>();
        ex.Which.Kind.Should().Be(LlmExceptionKind.Auth);
    }

    [Fact]
    public async Task FullPipeline_GenerateFicheStreaming_OpenAiCompatible_ProducesDocumentAndHtml()
    {
        // Realistic model response for a pedagogical lesson plan
        var modelJsonResponse = @"{
  ""metadata"": {
    ""title"": ""Les fractions décimales"",
    ""classLevel"": ""CM2"",
    ""subject"": ""Mathématiques"",
    ""durationMinutes"": 45,
    ""accentColor"": ""#2563EB"",
    ""date"": ""2026-08-29""
  },
  ""blocks"": [
    {
      ""$type"": ""heading"",
      ""level"": 1,
      ""runs"": [{ ""text"": ""Objectifs de la séance"" }]
    },
    {
      ""$type"": ""paragraph"",
      ""runs"": [{ ""text"": ""Comprendre le passage de la fraction décimale à l'écriture à virgule."" }]
    },
    {
      ""$type"": ""table"",
      ""headers"": [""Fraction"", ""Écriture décimale"", ""Lecture""],
      ""rows"": [
        [""1/10"", ""0,1"", ""Un dixième""],
        [""1/100"", ""0,01"", ""Un centième""]
      ]
    }
  ]
}";

        // SSE chunks
        var chunk1 = modelJsonResponse.Substring(0, 100);
        var chunk2 = modelJsonResponse.Substring(100, 150);
        var chunk3 = modelJsonResponse.Substring(250);

        var escapeJson = (string s) => System.Text.Json.JsonSerializer.Serialize(s);

        var sseBody = $"data: {{\"choices\":[{{\"delta\":{{\"content\":{escapeJson(chunk1)}}}}}]}}\n\n" +
                      $"data: {{\"choices\":[{{\"delta\":{{\"content\":{escapeJson(chunk2)}}}}}]}}\n\n" +
                      $"data: {{\"choices\":[{{\"delta\":{{\"content\":{escapeJson(chunk3)}}}}}]}}\n\n" +
                      "data: [DONE]\n\n";

        _server
            .Given(Request.Create().WithPath("/chat/completions").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/event-stream")
                .WithBody(sseBody));

        var orchestrator = new FicheGen.Infrastructure.Services.GenerationOrchestrator(_llmClient);
        var config = CreateTestConfig(globalProvider: "proxy");
        var parameters = new FicheGen.Core.Prompts.FicheParameters(
            ClassLevel: "CM2",
            Subject: "Mathématiques",
            Topic: "Les fractions décimales",
            DurationMinutes: 45
        );

        var streamedChunks = new List<string>();
        var progress = new Progress<string>(c => streamedChunks.Add(c));

        // Act - Call the generation model through the full pipeline
        var result = await orchestrator.GenerateFicheStreamingAsync(
            parameters,
            config,
            chunkProgress: progress,
            ct: CancellationToken.None
        );

        // Assert - Verify full pipeline execution
        result.Should().NotBeNull();
        result.IsFallback.Should().BeFalse();
        result.Document.Metadata.Title.Should().Be("Les fractions décimales");
        result.Document.Metadata.ClassLevel.Should().Be("CM2");
        result.Document.Blocks.Should().HaveCount(3);
        result.PreviewHtml.Should().Contain("Les fractions décimales");
        result.PreviewHtml.Should().Contain("Objectifs de la séance");
        result.PreviewHtml.Should().Contain("Un dixième");
        result.RawResponse.Should().Be(modelJsonResponse);
        result.RawPrompt.Should().Contain("Génère une fiche pédagogique pour le niveau CM2");
        streamedChunks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FullPipeline_GenerateFiche_Gemini_ProducesDocumentAndHtml()
    {
        var modelJsonResponse = @"{
  ""metadata"": {
    ""title"": ""La Révolution française"",
    ""classLevel"": ""CM1"",
    ""subject"": ""Histoire"",
    ""durationMinutes"": 60,
    ""accentColor"": ""#7C2D12"",
    ""date"": ""2026-08-29""
  },
  ""blocks"": [
    {
      ""$type"": ""heading"",
      ""level"": 1,
      ""runs"": [{ ""text"": ""La prise de la Bastille"" }]
    },
    {
      ""$type"": ""paragraph"",
      ""runs"": [{ ""text"": ""Le 14 juillet 1789, le peuple parisien s'empare de la forteresse de la Bastille."" }]
    }
  ]
}";

        var geminiResponseJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = modelJsonResponse }
                        }
                    }
                }
            }
        });

        _server
            .Given(Request.Create().WithPath("/v1beta/models/gemini-2.5-pro:generateContent").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(geminiResponseJson));

        var orchestrator = new FicheGen.Infrastructure.Services.GenerationOrchestrator(_llmClient);
        var config = CreateTestConfig(globalProvider: "aistudio");
        var parameters = new FicheGen.Core.Prompts.FicheParameters(
            ClassLevel: "CM1",
            Subject: "Histoire",
            Topic: "La Révolution française",
            DurationMinutes: 60
        );

        // Act - Call the generation model through the full pipeline
        var result = await orchestrator.GenerateFicheAsync(
            parameters,
            config,
            ct: CancellationToken.None
        );

        // Assert - Verify full pipeline execution
        result.Should().NotBeNull();
        result.IsFallback.Should().BeFalse();
        result.Document.Metadata.Title.Should().Be("La Révolution française");
        result.Document.Metadata.ClassLevel.Should().Be("CM1");
        result.Document.Blocks.Should().HaveCount(2);
        result.PreviewHtml.Should().Contain("La Révolution française");
        result.PreviewHtml.Should().Contain("La prise de la Bastille");
        result.PreviewHtml.Should().Contain("14 juillet 1789");
        result.RawResponse.Should().Be(modelJsonResponse);
        result.RawPrompt.Should().Contain("Génère une fiche pédagogique pour le niveau CM1");
    }

    private class TestLlmRouter : LlmRouter
    {
        private readonly string _baseUrl;
        public TestLlmRouter(string baseUrl) => _baseUrl = baseUrl;

        public override Core.Ai.Routing.ProviderRoute Resolve(string purpose, AiRequestConfig cfg, bool isStreaming = false)
        {
            var baseRoute = base.Resolve(purpose, cfg, isStreaming);
            var path = new Uri(baseRoute.Endpoint).AbsolutePath;
            var newEndpoint = $"{_baseUrl}{path}";
            return baseRoute with { Endpoint = newEndpoint };
        }
    }
}
