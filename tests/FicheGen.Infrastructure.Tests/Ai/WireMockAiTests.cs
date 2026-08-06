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
