using FicheGen.Core.Documents;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Documents;

public class FallbackMarkdownRendererTests
{
    [Fact]
    public void ConvertMarkdownToDocument_WithWarningCallout_ReturnsWarningCalloutAndEmptyDocument()
    {
        var docNull = FallbackMarkdownRenderer.ConvertMarkdownToDocument(null!, "Test Title", includeWarningCallout: true);
        docNull.Should().NotBeNull();
        docNull.Metadata.Title.Should().Be("Test Title");
        docNull.Blocks.Should().HaveCount(1);
        docNull.Blocks[0].Should().BeOfType<CalloutBoxBlock>();

        var docEmpty = FallbackMarkdownRenderer.ConvertMarkdownToDocument("   ", "Empty Title", includeWarningCallout: true);
        docEmpty.Should().NotBeNull();
        docEmpty.Metadata.Title.Should().Be("Empty Title");
        docEmpty.Blocks.Should().HaveCount(1);
    }

    [Fact]
    public void ConvertMarkdownToDocument_WithoutWarningCallout_ReturnsEmptyBlocksWhenContentEmpty()
    {
        var doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument("   ", "Empty Title", includeWarningCallout: false);
        doc.Should().NotBeNull();
        doc.Blocks.Should().BeEmpty();
    }

    [Fact]
    public void ConvertMarkdownToDocument_HeadingsAndParagraphs_ParsedCorrectly()
    {
        var md = @"# Titre Principal
Voici un paragraphe d'introduction avec du **texte en gras** et du texte normal.

## Sous-titre 1
Autre paragraphe simple.

### Sous-titre 2
Dernier paragraphe.";

        var doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(md, "Fiche Séances", includeWarningCallout: false);

        doc.Metadata.Title.Should().Be("Fiche Séances");
        // H1 + P + H2 + P + H3 + P = 6 blocks
        doc.Blocks.Should().HaveCount(6);

        var h1 = doc.Blocks[0] as HeadingBlock;
        h1.Should().NotBeNull();
        h1!.Level.Should().Be(1);
        h1.Runs.Should().ContainSingle().Which.Text.Should().Be("Titre Principal");

        var p1 = doc.Blocks[1] as ParagraphBlock;
        p1.Should().NotBeNull();
        p1!.Runs.Should().HaveCount(3);
        p1.Runs[0].Text.Should().Be("Voici un paragraphe d'introduction avec du ");
        p1.Runs[0].IsBold.Should().BeFalse();
        p1.Runs[1].Text.Should().Be("texte en gras");
        p1.Runs[1].IsBold.Should().BeTrue();
        p1.Runs[2].Text.Should().Be(" et du texte normal.");

        var h2 = doc.Blocks[2] as HeadingBlock;
        h2.Should().NotBeNull();
        h2!.Level.Should().Be(2);

        var h3 = doc.Blocks[4] as HeadingBlock;
        h3.Should().NotBeNull();
        h3!.Level.Should().Be(3);
    }

    [Fact]
    public void ConvertMarkdownToDocument_BulletLists_GroupedProperly()
    {
        var md = @"# Liste d'objectifs
* Premier objectif
* Deuxième **objectif important**
* Troisième objectif

Paragraphe intermédiaire.

- Autre puce avec tiret
+ Puce avec plus
• Puce unicode";

        var doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(md, includeWarningCallout: false);

        doc.Blocks.Should().HaveCount(4); // H1 + BulletList1 + P + BulletList2

        var list1 = doc.Blocks[1] as BulletListBlock;
        list1.Should().NotBeNull();
        list1!.Items.Should().HaveCount(3);
        list1.Items[0][0].Text.Should().Be("Premier objectif");
        list1.Items[1][1].Text.Should().Be("objectif important");
        list1.Items[1][1].IsBold.Should().BeTrue();

        var list2 = doc.Blocks[3] as BulletListBlock;
        list2.Should().NotBeNull();
        list2!.Items.Should().HaveCount(3);
    }

    [Fact]
    public void ConvertMarkdownToDocument_NumberedLists_GroupedProperly()
    {
        var md = @"1. Étape un
2. Étape deux
3. Étape trois";

        var doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(md, includeWarningCallout: false);

        doc.Blocks.Should().HaveCount(1);
        var nl = doc.Blocks[0] as NumberedListBlock;
        nl.Should().NotBeNull();
        nl!.Items.Should().HaveCount(3);
        nl.Items[0][0].Text.Should().Be("Étape un");
        nl.Items[1][0].Text.Should().Be("Étape deux");
        nl.Items[2][0].Text.Should().Be("Étape trois");
    }

    [Fact]
    public void ConvertMarkdownToDocument_Table_ParsedCorrectly()
    {
        var md = @"| Étape | Durée | Matériel |
| --- | --- | --- |
| Découverte | 15 min | Cahier |
| Application | 30 min | Feuilles |";

        var doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(md, includeWarningCallout: false);

        doc.Blocks.Should().HaveCount(1);
        var tbl = doc.Blocks[0] as TableBlock;
        tbl.Should().NotBeNull();
        tbl!.Headers.Should().Equal("Étape", "Durée", "Matériel");
        tbl.Rows.Should().HaveCount(2);
        tbl.Rows[0].Should().Equal("Découverte", "15 min", "Cahier");
        tbl.Rows[1].Should().Equal("Application", "30 min", "Feuilles");
    }

    [Fact]
    public void ConvertMarkdownToDocument_CalloutAndPageBreak_ParsedCorrectly()
    {
        var md = @"[INFO]
Ceci est une consigne importante.
• Puce interne

[SAUT DE PAGE]

Dernier paragraphe.";

        var doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(md, includeWarningCallout: false);

        doc.Blocks.Should().HaveCount(3);
        var callout = doc.Blocks[0] as CalloutBoxBlock;
        callout.Should().NotBeNull();
        callout!.Kind.Should().Be("info");
        callout.ContentBlocks.Should().HaveCount(2);

        doc.Blocks[1].Should().BeOfType<PageBreakBlock>();
        doc.Blocks[2].Should().BeOfType<ParagraphBlock>();
    }

    [Fact]
    public void ConvertMarkdownToDocument_KeyValueGrid_ParsedCorrectly()
    {
        var md = @"Durée : 50 minutes
Niveau : CM2
Matière : Mathématiques";

        var doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(md, includeWarningCallout: false);

        doc.Blocks.Should().HaveCount(1);
        var kv = doc.Blocks[0] as KeyValueGridBlock;
        kv.Should().NotBeNull();
        kv!.Pairs.Should().HaveCount(3);
        kv.Pairs[0].Key.Should().Be("Durée");
        kv.Pairs[0].Value.Should().Be("50 minutes");
        kv.Pairs[1].Key.Should().Be("Niveau");
        kv.Pairs[1].Value.Should().Be("CM2");
        kv.Pairs[2].Key.Should().Be("Matière");
        kv.Pairs[2].Value.Should().Be("Mathématiques");
    }

    [Fact]
    public void GeneratedDocument_MarkdownRoundTrip_PreservesAllBlockTypes()
    {
        var meta = new DocumentMetadata("Fiche Complète", "Sous-titre");
        var originalBlocks = new Block[]
        {
            new HeadingBlock(1, "1. Découverte"),
            new ParagraphBlock("Texte explicatif pour la séance."),
            new KeyValueGridBlock(new List<KeyValuePair<string, string>>
            {
                new("Durée", "45 min"),
                new("Matériel", "Cahier d'exercices")
            }),
            new BulletListBlock(new List<List<TextRun>>
            {
                new() { new("Item A") },
                new() { new("Item B") }
            }),
            new TableBlock(new List<string> { "H1", "H2" }, new List<List<string>> { new() { "R1C1", "R1C2" } }),
            new CalloutBoxBlock("warning", new List<Block> { new ParagraphBlock("Attention particulière.") }),
            new PageBreakBlock()
        };

        var originalDoc = new GeneratedDocument(meta, originalBlocks);
        var markdownText = originalDoc.ToBodyPlainText();

        var roundTrippedDoc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(markdownText, meta.Title, includeWarningCallout: false);

        roundTrippedDoc.Blocks.Should().HaveCount(7);
        roundTrippedDoc.Blocks[0].Should().BeOfType<HeadingBlock>();
        roundTrippedDoc.Blocks[1].Should().BeOfType<ParagraphBlock>();
        roundTrippedDoc.Blocks[2].Should().BeOfType<KeyValueGridBlock>();
        roundTrippedDoc.Blocks[3].Should().BeOfType<BulletListBlock>();
        roundTrippedDoc.Blocks[4].Should().BeOfType<TableBlock>();
        roundTrippedDoc.Blocks[5].Should().BeOfType<CalloutBoxBlock>();
        roundTrippedDoc.Blocks[6].Should().BeOfType<PageBreakBlock>();
    }
}
