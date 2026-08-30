using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.Core.Ai;
using FicheGen.Infrastructure.Ai;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests.Ai;

public sealed class LlmRouterTests
{
    [Theory]
    [InlineData("cloud", "https://bbodlidtaosxeyeovixe.supabase.co/functions/v1/chat", "supabase_access_token")]
    [InlineData("openai", "https://api.openai.com/v1/chat/completions", "openai_api_key")]
    [InlineData("deepseek", "https://api.deepseek.com/v1/chat/completions", "deepseek_api_key")]
    [InlineData("groq", "https://api.groq.com/openai/v1/chat/completions", "groq_api_key")]
    [InlineData("mistral", "https://api.mistral.ai/v1/chat/completions", "mistral_api_key")]
    [InlineData("openrouter", "https://openrouter.ai/api/v1/chat/completions", "openrouter_api_key")]
    [InlineData("together", "https://api.together.xyz/v1/chat/completions", "together_api_key")]
    [InlineData("fireworks", "https://api.fireworks.ai/inference/v1/chat/completions", "fireworks_api_key")]
    [InlineData("cerebras", "https://api.cerebras.ai/v1/chat/completions", "cerebras_api_key")]
    [InlineData("xai", "https://api.x.ai/v1/chat/completions", "xai_api_key")]
    [InlineData("doubleword", "https://api.doubleword.ai/v1/chat/completions", "doubleword_api_key")]
    [InlineData("anthropic", "https://api.anthropic.com/v1/chat/completions", "anthropic_api_key")]
    [InlineData("aistudio", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent", "gemini_api_key")]
    [InlineData("vercel", "http://localhost:11434/v1/api/chat", "vercel_api_key")]
    [InlineData("proxy", "http://localhost:11434/v1/chat/completions", "proxy_api_key")]
    public void Resolve_AllDisplayedProviders_ResolvesCorrectEndpointAndSecretKey(string provider, string expectedEndpointPrefix, string expectedSecretKey)
    {
        var router = new LlmRouter();

        var config = new AiRequestConfig(
            GlobalProvider: provider,
            DefaultModels: new Dictionary<string, string>(),
            RoutingOverrides: new Dictionary<string, RoutingOverride>(),
            ProxyBaseUrl: "http://localhost:11434/v1",
            VertexProject: "test-proj",
            VertexRegion: "europe-west1",
            Temperatures: new Dictionary<string, double> { { "generation", 0.7 } },
            SecretResolver: (k, _) => ValueTask.FromResult<string?>("secret_token_123")
        );

        var route = router.Resolve("generation", config, isStreaming: false);

        route.Should().NotBeNull();
        route.Endpoint.Should().StartWith(expectedEndpointPrefix);
        route.SecretKeyName.Should().Be(expectedSecretKey);
    }
}
