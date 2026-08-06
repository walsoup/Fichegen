using FicheGen.Core.Documents;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Documents;

public class JsonCleanerTests
{
    [Fact]
    public void Clean_StripsMarkdownFencesAndExtractsJsonObject()
    {
        var raw = @"Voici le JSON généré :
```json
{
  ""metadata"": { ""title"": ""Fiche Test"" },
  ""blocks"": [],
}
```
J'espère que cela vous convient !";

        var cleaned = JsonCleaner.Clean(raw);

        cleaned.Should().StartWith("{");
        cleaned.Should().EndWith("}");
        cleaned.Should().NotContain("```");
        cleaned.Should().NotContain(",\n}");
    }

    [Fact]
    public void TryDeserializeDocument_ValidJson_ReturnsTrueAndPopulatesDocument()
    {
        var json = @"{
          ""metadata"": { ""title"": ""Fiche Geometrie"", ""classLevel"": ""6e"" },
          ""blocks"": [
            { ""$type"": ""heading"", ""level"": 1, ""runs"": [{ ""text"": ""Les Angles"" }] }
          ]
        }";

        var success = JsonCleaner.TryDeserializeDocument(json, out var doc);

        success.Should().BeTrue();
        doc.Should().NotBeNull();
        doc!.Metadata.Title.Should().Be("Fiche Geometrie");
        doc.Blocks.Should().HaveCount(1);
    }
}
