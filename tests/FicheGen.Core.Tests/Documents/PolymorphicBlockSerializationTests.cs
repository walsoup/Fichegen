using System.Text.Json;
using FicheGen.Core.Documents;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Documents;

public class PolymorphicBlockSerializationTests
{
    [Fact]
    public void GeneratedDocument_FullPolymorphicRoundtrip_Succeeds()
    {
        var meta = new DocumentMetadata(
            Title: "Séance sur le système solaire",
            Subtitle: "Cycle 3 - CM2",
            ClassLevel: "CM2",
            Subject: "Sciences",
            Duration: 55,
            Date: "2026-03-01",
            DocType: "fiche"
        );

        var blocks = new List<Block>
        {
            new HeadingBlock(1, new List<TextRun>
            {
                new("Introduction aux ", IsBold: true),
                new("Planètes", IsItalic: true)
            }),
            new ParagraphBlock(new List<TextRun>
            {
                new("Le système solaire est composé de 8 planètes orbitant autour du Soleil.")
            }),
            new BulletListBlock(new List<List<TextRun>>
            {
                new() { new("Mercure, Vénus, Terre, Mars") },
                new() { new("Jupiter, Saturne, Uranus, Neptune") }
            }),
            new NumberedListBlock(new List<List<TextRun>>
            {
                new() { new("Observation de la vidéo") },
                new() { new("Schématisation dans le cahier") }
            }),
            new KeyValueGridBlock(new List<KeyValuePair<string, string>>
            {
                new("Durée", "55 min"),
                new("Matériel", "Vidéoprojecteur, fiches d'exercices")
            }),
            new TableBlock(
                Headers: new List<string> { "��tape", "Activité élève", "Rôle enseignant" },
                Rows: new List<List<string>>
                {
                    new() { "1", "Découverte", "Animation" },
                    new() { "2", "Application", "Guidage" }
                }
            ),
            new CalloutBoxBlock(
                Kind: "tip",
                ContentBlocks: new List<Block>
                {
                    new ParagraphBlock("Astuce DYS : Fournir un schéma pré-rempli.")
                }
            ),
            new PageBreakBlock()
        };

        var doc = new GeneratedDocument(meta, blocks, null);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(doc, options);
        json.Should().Contain("\"$type\": \"heading\"");
        json.Should().Contain("\"$type\": \"paragraph\"");
        json.Should().Contain("\"$type\": \"bulletList\"");
        json.Should().Contain("\"$type\": \"numberedList\"");
        json.Should().Contain("\"$type\": \"keyValueGrid\"");
        json.Should().Contain("\"$type\": \"table\"");
        json.Should().Contain("\"$type\": \"calloutBox\"");
        json.Should().Contain("\"$type\": \"pageBreak\"");

        var deserialized = JsonSerializer.Deserialize<GeneratedDocument>(json, options);
        deserialized.Should().NotBeNull();
        deserialized!.Metadata.Title.Should().Be(meta.Title);
        deserialized.Blocks.Should().HaveCount(8);

        deserialized.Blocks[0].Should().BeOfType<HeadingBlock>();
        deserialized.Blocks[1].Should().BeOfType<ParagraphBlock>();
        deserialized.Blocks[2].Should().BeOfType<BulletListBlock>();
        deserialized.Blocks[3].Should().BeOfType<NumberedListBlock>();
        deserialized.Blocks[4].Should().BeOfType<KeyValueGridBlock>();
        deserialized.Blocks[5].Should().BeOfType<TableBlock>();
        deserialized.Blocks[6].Should().BeOfType<CalloutBoxBlock>();
        deserialized.Blocks[7].Should().BeOfType<PageBreakBlock>();
    }

    [Fact]
    public void GeneratedDocument_ToPlainText_FormatsAllBlockTypes()
    {
        var meta = new DocumentMetadata("Titre", "Sous-titre");
        var blocks = new List<Block>
        {
            new HeadingBlock(1, "Grand 1"),
            new ParagraphBlock("Ceci est un paragraphe."),
            new BulletListBlock(new List<List<TextRun>> { new() { new("Puce A") } }),
            new NumberedListBlock(new List<List<TextRun>> { new() { new("Item 1") } }),
            new KeyValueGridBlock(new List<KeyValuePair<string, string>> { new("Clé", "Valeur") }),
            new TableBlock(new List<string> { "Col1", "Col2" }, new List<List<string>> { new() { "A", "B" } }),
            new CalloutBoxBlock("warning", new List<Block> { new ParagraphBlock("Attention !") })
        };

        var doc = new GeneratedDocument(meta, blocks);
        var plain = doc.ToPlainText();

        plain.Should().Contain("Titre");
        plain.Should().Contain("Sous-titre");
        plain.Should().Contain("# Grand 1");
        plain.Should().Contain("Ceci est un paragraphe.");
        plain.Should().Contain("• Puce A");
        plain.Should().Contain("1. Item 1");
        plain.Should().Contain("Clé : Valeur");
        plain.Should().Contain("Col1 | Col2");
        plain.Should().Contain("A | B");
        plain.Should().Contain("[WARNING]");
        plain.Should().Contain("Attention !");
    }
}
