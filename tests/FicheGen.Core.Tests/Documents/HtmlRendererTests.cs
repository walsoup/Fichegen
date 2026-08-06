using FicheGen.Core.Documents;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Documents;

public class HtmlRendererTests
{
    [Fact]
    public void RenderToFragment_GeneratesSemanticHtmlMarkup()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Fiche de Calcul", ClassLevel: "CM1", Subject: "Maths", Duration: 30),
            Blocks: new List<Block>
            {
                new HeadingBlock(1, "1. Découverte"),
                new ParagraphBlock(new List<TextRun>
                {
                    new TextRun("Consigne : ", IsBold: true),
                    new TextRun("Effectuer les opérations suivantes.")
                }),
                new CalloutBoxBlock("corrige", new List<Block>
                {
                    new ParagraphBlock("Corrigé : 12 + 15 = 27")
                })
            }
        );

        var html = HtmlRenderer.RenderToFragment(doc);

        html.Should().Contain("<h1 class=\"doc-title\">Fiche de Calcul</h1>");
        html.Should().Contain("<span class=\"badge level-badge\">Niveau: CM1</span>");
        html.Should().Contain("<h1>1. Découverte</h1>");
        html.Should().Contain("<strong>Consigne : </strong>");
        html.Should().Contain("<div class=\"callout callout-corrige\">");
    }

    [Fact]
    public void RenderToFullHtml_IncludesDocTypeStyleAndBody()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Evaluation CM2"),
            Blocks: new List<Block> { new ParagraphBlock("Texte de test.") }
        );

        var fullHtml = HtmlRenderer.RenderToFullHtml(doc);

        fullHtml.Should().StartWith("<!DOCTYPE html>");
        fullHtml.Should().Contain("<title>Evaluation CM2</title>");
        fullHtml.Should().Contain("<style>");
        fullHtml.Should().Contain("<article class=\"fiche-content\">");
    }
}
