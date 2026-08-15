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

    [Fact]
    public void RenderToFragment_StudentVersion_HidesCorrectionAndAddsStudentHeader()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Contrôle de fractions", ClassLevel: "6e", Subject: "Mathématiques"),
            Blocks: new List<Block>
            {
                new HeadingBlock(1, "Exercice 1"),
                new ParagraphBlock("Calculer 1/2 + 1/4."),
                new CalloutBoxBlock("corrige", new List<Block>
                {
                    new ParagraphBlock("Réponse : 3/4")
                }),
                new HeadingBlock(2, "Corrigé détaillé"),
                new ParagraphBlock("Explications pour le professeur.")
            }
        );

        var studentHtml = HtmlRenderer.RenderToFragment(doc, isStudentVersion: true);

        // Student header present
        studentHtml.Should().Contain("student-header-box");
        studentHtml.Should().Contain("Nom :");
        studentHtml.Should().Contain("Prénom :");
        studentHtml.Should().Contain("Note :");

        // Title suffix
        studentHtml.Should().Contain("Contrôle de fractions — Version Élève");

        // Exercise present
        studentHtml.Should().Contain("Exercice 1");
        studentHtml.Should().Contain("Calculer 1/2 + 1/4.");

        // Correction callout converted to student-answer-box
        studentHtml.Should().Contain("student-answer-box");
        studentHtml.Should().Contain("dots-line");
        studentHtml.Should().NotContain("Réponse : 3/4");

        // Correction heading and blocks stripped
        studentHtml.Should().NotContain("Corrigé détaillé");
        studentHtml.Should().NotContain("Explications pour le professeur.");
    }

    [Fact]
    public void RenderToFullHtml_IncludesCspMetaTag()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Fiche Test"),
            Blocks: new List<Block> { new ParagraphBlock("Contenu") }
        );

        var fullHtml = HtmlRenderer.RenderToFullHtml(doc);

        fullHtml.Should().Contain("Content-Security-Policy");
        fullHtml.Should().Contain("default-src 'none'");
        fullHtml.Should().Contain("style-src 'unsafe-inline'");
    }

    [Fact]
    public void RenderToFragment_HtmlEncodesMetadata_ToPreventXss()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("<script>alert(1)</script>", ClassLevel: "<img src=x onerror=alert(2)>", Subject: "Maths & Physics"),
            Blocks: new List<Block> { new ParagraphBlock("Test") }
        );

        var fragment = HtmlRenderer.RenderToFragment(doc);

        fragment.Should().NotContain("<script>alert(1)</script>");
        fragment.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        fragment.Should().NotContain("<img src=x");
        fragment.Should().Contain("&lt;img src=x");
    }
}
