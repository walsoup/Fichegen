using System.Text.Json;

using FicheGen.Core.Documents;

using FluentAssertions;

using Xunit;


namespace FicheGen.Core.Tests.Documents;


public class GeneratedDocumentTests

{

    [Fact]

    public void GeneratedDocument_JsonSerialization_RoundTripsPolymorphicBlocks()

    {

        var doc = new GeneratedDocument(

            Metadata: new DocumentMetadata("Fiche de fractions", Subtitle: "CM2 - Mathématiques", ClassLevel: "CM2", Subject: "Maths", Duration: 45),

            Blocks: new List<Block>

            {

                new HeadingBlock(1, "Objectifs"),

                new BulletListBlock(new List<List<TextRun>>

                {

                    new() { new TextRun("Comprendre les fractions", IsBold: true) },

                    new() { new TextRun("Comparer deux fractions") }

                }),

                new TableBlock(

                    Headers: new List<string> { "Étape", "Durée" },

                    Rows: new List<List<string>>

                    {

                        new() { "Découverte", "15 min" },

                        new() { "Exercices", "30 min" }

                    }

                ),

                new CalloutBoxBlock("differentiation", new List<Block>

                {

                    new ParagraphBlock("Aide personnalisée pour le groupe B.")

                })

            }

        );


        var options = new JsonSerializerOptions { WriteIndented = true };

        var json = JsonSerializer.Serialize(doc, options);


        json.Should().Contain("\"$type\": \"heading\"");

        json.Should().Contain("\"$type\": \"bulletList\"");

        json.Should().Contain("\"$type\": \"table\"");

        json.Should().Contain("\"$type\": \"calloutBox\"");


        var deserialized = JsonSerializer.Deserialize<GeneratedDocument>(json, options);

        deserialized.Should().NotBeNull();

        deserialized!.Metadata.Title.Should().Be("Fiche de fractions");

        deserialized.Blocks.Should().HaveCount(4);

        deserialized.Blocks[0].Should().BeOfType<HeadingBlock>();

        deserialized.Blocks[3].Should().BeOfType<CalloutBoxBlock>();

    }


    [Fact]

    public void ToPlainText_ProducesFormattedReadableString()

    {

        var doc = new GeneratedDocument(

            Metadata: new DocumentMetadata("Fiche de Test"),

            Blocks: new List<Block>

            {

                new HeadingBlock(1, "Titre 1"),

                new ParagraphBlock("Ceci est un paragraphe."),

                new BulletListBlock(new List<List<TextRun>>

                {

                    new() { new TextRun("Puce 1") },

                    new() { new TextRun("Puce 2") }

                })

            }

        );


        var text = doc.ToPlainText();


        text.Should().Contain("Fiche de Test");

        text.Should().Contain("# Titre 1");

        text.Should().Contain("Ceci est un paragraphe.");

        text.Should().Contain("• Puce 1");

    }



    [Fact]

    public void ToPlainText_WithPageBreak_EmitsMarker()

    {

        var doc = new GeneratedDocument(

            Metadata: new DocumentMetadata("Fiche avec saut"),

            Blocks: new List<Block>

            {

                new ParagraphBlock("Page un."),

                new PageBreakBlock(),

                new ParagraphBlock("Page deux.")

            }

        );


        var text = doc.ToPlainText();


        text.Should().Contain("[SAUT DE PAGE]");

        text.IndexOf("[SAUT DE PAGE]").Should().BeGreaterThan(text.IndexOf("Page un."));

        text.IndexOf("[SAUT DE PAGE]").Should().BeLessThan(text.IndexOf("Page deux."));

    }

    [Fact]
    public void ToBodyPlainText_ExcludesTitleAndSubtitle_ContainsOnlyBlockContent()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Grand Titre", Subtitle: "Sous-titre descriptif"),
            Blocks: new List<Block>
            {
                new HeadingBlock(2, "Section 1"),
                new ParagraphBlock("Contenu de la section."),
                new NumberedListBlock(new List<List<TextRun>>
                {
                    new() { new TextRun("Étape 1") },
                    new() { new TextRun("Étape 2") }
                })
            }
        );

        var body = doc.ToBodyPlainText();

        body.Should().NotContain("Grand Titre");
        body.Should().NotContain("=====");
        body.Should().NotContain("Sous-titre descriptif");
        body.Should().Contain("## Section 1");
        body.Should().Contain("Contenu de la section.");
        body.Should().Contain("1. Étape 1");
        body.Should().Contain("2. Étape 2");
    }

    [Fact]
    public void ManualEdit_MarkdownRoundTrip_PreservesBlocksWithoutWarningCallout()
    {
        var original = new GeneratedDocument(
            Metadata: new DocumentMetadata("Fiche Originale", Subtitle: "Objectifs"),
            Blocks: new List<Block>
            {
                new HeadingBlock(1, "1. Découverte"),
                new ParagraphBlock("Texte explicatif initial."),
                new BulletListBlock(new List<List<TextRun>>
                {
                    new() { new TextRun("Point A") },
                    new() { new TextRun("Point B") }
                })
            }
        );

        var body = original.ToBodyPlainText();
        var editedBody = body.Replace("Point B", "Point B Modifié");

        var newDoc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(editedBody, "Fiche Modifiée", includeWarningCallout: false);

        newDoc.Metadata.Title.Should().Be("Fiche Modifiée");
        newDoc.Blocks.Should().HaveCount(3);
        newDoc.Blocks.Should().NotContain(b => b is CalloutBoxBlock && ((CalloutBoxBlock)b).Kind == "warning");
        newDoc.Blocks[0].Should().BeOfType<HeadingBlock>();
        newDoc.Blocks[1].Should().BeOfType<ParagraphBlock>();
        var list = newDoc.Blocks[2].Should().BeOfType<BulletListBlock>().Subject;
        list.Items[1][0].Text.Should().Be("Point B Modifié");
    }
}
