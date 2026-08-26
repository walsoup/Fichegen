using FicheGen.Core.Documents;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Documents;

public class FallbackMarkdownRendererTests
{
    [Fact]
    public void ConvertMarkdownToDocument_EmptyOrNull_ReturnsWarningCalloutAndEmptyDocument()
    {
        var docNull = FallbackMarkdownRenderer.ConvertMarkdownToDocument(null!, "Test Title");
        docNull.Should().NotBeNull();
        docNull.Metadata.Title.Should().Be("Test Title");
        docNull.Blocks.Should().HaveCount(1);
        docNull.Blocks[0].Should().BeOfType<CalloutBoxBlock>();

        var docEmpty = FallbackMarkdownRenderer.ConvertMarkdownToDocument("   ", "Empty Title");
        docEmpty.Should().NotBeNull();
        docEmpty.Metadata.Title.Should().Be("Empty Title");
        docEmpty.Blocks.Should().HaveCount(1);
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

        var doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(md, "Fiche Séances");

        doc.Metadata.Title.Should().Be("Fiche Séances");
        // Warning callout + H1 + P + H2 + P + H3 + P = 7 blocks
        doc.Blocks.Should().HaveCount(7);

        var h1 = doc.Blocks[1] as HeadingBlock;
        h1.Should().NotBeNull();
        h1!.Level.Should().Be(1);
        h1.Runs.Should().ContainSingle().Which.Text.Should().Be("Titre Principal");

        var p1 = doc.Blocks[2] as ParagraphBlock;
        p1.Should().NotBeNull();
        p1!.Runs.Should().HaveCount(3);
        p1.Runs[0].Text.Should().Be("Voici un paragraphe d'introduction avec du ");
        p1.Runs[0].IsBold.Should().BeFalse();
        p1.Runs[1].Text.Should().Be("texte en gras");
        p1.Runs[1].IsBold.Should().BeTrue();
        p1.Runs[2].Text.Should().Be(" et du texte normal.");

        var h2 = doc.Blocks[3] as HeadingBlock;
        h2.Should().NotBeNull();
        h2!.Level.Should().Be(2);

        var h3 = doc.Blocks[5] as HeadingBlock;
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
+ Puce avec plus";

        var doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(md);

        doc.Blocks.Should().HaveCount(5); // Callout + H1 + BulletList1 + P + BulletList2

        var list1 = doc.Blocks[2] as BulletListBlock;
        list1.Should().NotBeNull();
        list1!.Items.Should().HaveCount(3);
        list1.Items[0][0].Text.Should().Be("Premier objectif");
        list1.Items[1][1].Text.Should().Be("objectif important");
        list1.Items[1][1].IsBold.Should().BeTrue();

        var list2 = doc.Blocks[4] as BulletListBlock;
        list2.Should().NotBeNull();
        list2!.Items.Should().HaveCount(2);
    }
}
