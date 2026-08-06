using System.Text.Json;
using FicheGen.Core.Documents;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests;

public class GeneratedDocumentTests
{
    [Fact]
    public void DocumentSerialization_ShouldRoundtripSuccessfully()
    {
        // Arrange
        var metadata = new DocumentMetadata("Fiche de Numération", "Les fractions", "CM2", "Mathématiques", 45);
        var blocks = new List<Block>
        {
            new HeadingBlock(1, new List<TextRun> { new("Objectifs pédagogiques", IsBold: true) }),
            new ParagraphBlock(new List<TextRun> { new("Comprendre le rôle du numérateur et du dénominateur.") }),
            new BulletListBlock(new List<List<TextRun>>
            {
                new() { new("Matériel: Bandes de papier") },
                new() { new("Durée: 45 min") }
            }),
            new CalloutBoxBlock("Corrigé", new List<Block>
            {
                new ParagraphBlock(new List<TextRun> { new("1/2 est égal à 2/4.") })
            })
        };
        var doc = new GeneratedDocument(metadata, blocks, "{}");

        // Act
        var json = JsonSerializer.Serialize(doc);
        var deserialized = JsonSerializer.Deserialize<GeneratedDocument>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Metadata.Title.Should().Be("Fiche de Numération");
        deserialized.Blocks.Should().HaveCount(4);
        deserialized.Blocks[0].Should().BeOfType<HeadingBlock>();
        deserialized.Blocks[3].Should().BeOfType<CalloutBoxBlock>();
    }
}
