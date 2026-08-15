using System.Collections.Immutable;
using FicheGen.Core.Ai;
using FicheGen.Core.Ai.Routing;
using FicheGen.Core.Documents;
using FicheGen.Core.Storage;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests;

public class M1Phase1CoreStressTests
{
    [Fact]
    public void AppSettings_Structure_IsProperlyInitialized()
    {
        var settings = new AppSettings();
        settings.SchemaVersion.Should().BeGreaterThanOrEqualTo(3);
        settings.Ui.Should().NotBeNull();
        settings.Defaults.Should().NotBeNull();
        settings.Ai.Should().NotBeNull();
        settings.Folders.Should().NotBeNull();
        settings.Features.Should().NotBeNull();
    }

    [Fact]
    public void GeneratedDocument_BlocksCollection_IsReadOnlyOrImmutable()
    {
        var doc = new GeneratedDocument(
            new DocumentMetadata("Title"),
            new List<Block> { new ParagraphBlock("Hello") }
        );

        var prop = typeof(GeneratedDocument).GetProperty("Blocks");
        prop.Should().NotBeNull();
        
        // Check if property type is IReadOnlyList or ImmutableList or IReadOnlyCollection
        var type = prop!.PropertyType;
        bool isReadOnly = typeof(IReadOnlyList<Block>).IsAssignableFrom(type) ||
                          type.Name.StartsWith("IReadOnly") ||
                          type.Name.StartsWith("Immutable");

        isReadOnly.Should().BeTrue("GeneratedDocument.Blocks must be a read-only or immutable collection");
        
        // Ensure property does NOT allow calling Add directly as List<Block>
        type.Should().NotBe(typeof(List<Block>), "GeneratedDocument.Blocks should not be mutable List<Block>");
    }

    [Fact]
    public void AiRequestConfig_ToString_RedactsApiKeysAndSecrets()
    {
        var config = new AiRequestConfig(
            GlobalProvider: "aistudio",
            DefaultModels: new Dictionary<string, string>(),
            RoutingOverrides: new Dictionary<string, RoutingOverride>(),
            ProxyBaseUrl: "http://localhost:11434/v1",
            VertexProject: "my-project",
            VertexRegion: "us-central1",
            Temperatures: new Dictionary<string, double>(),
            SecretResolver: (key, ct) => ValueTask.FromResult<string?>("sk-secret-key-12345")
        );

        var str = config.ToString();
        str.Should().NotContain("sk-secret-key-12345", "AiRequestConfig.ToString() must redact API keys and secrets");
    }

    [Fact]
    public void ProviderRoute_ToString_RedactsSecrets()
    {
        var route = new ProviderRoute(
            AdapterKind: ProviderAdapterKind.Gemini,
            Model: "gemini-3.6-flash",
            Endpoint: "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent?key=secret_api_key_999",
            AuthStrategy: AuthStrategyKind.ApiKeyQuery,
            SecretKeyName: "gemini_api_key"
        );

        var str = route.ToString();
        str.Should().NotContain("secret_api_key_999", "ProviderRoute.ToString() must redact secrets in Endpoint or secret properties");
    }
}
