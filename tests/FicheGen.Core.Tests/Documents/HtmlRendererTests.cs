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
    public void RenderToFragment_StudentVersion_SubHeadingInsideCorrectionDoesNotLeakContent()
    {
        // Regression: a sub-heading (h3) inside a correction section (h2) must NOT reset
        // skippingCorrectionSection — only a sibling or higher-level heading should.
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Test"),
            Blocks: new List<Block>
            {
                new HeadingBlock(2, "Exercice 1"),
                new ParagraphBlock("Énoncé de l'exercice."),
                new HeadingBlock(2, "Corrigé"),
                new HeadingBlock(3, "Étape 1 : Calculs"),       // child heading — must stay hidden
                new ParagraphBlock("Réponse secrète : 42."),    // must stay hidden
                new HeadingBlock(2, "Exercice 2"),              // same level — ends correction
                new ParagraphBlock("Second exercice visible."),
            }
        );

        var studentHtml = HtmlRenderer.RenderToFragment(doc, isStudentVersion: true);

        studentHtml.Should().Contain("Exercice 1");
        studentHtml.Should().Contain("Énoncé de l&#39;exercice.");  // ' encoded as &#39; by EncodeText
        studentHtml.Should().NotContain("Corrigé");
        studentHtml.Should().NotContain("Étape 1 : Calculs");
        studentHtml.Should().NotContain("Réponse secrète : 42.");
        studentHtml.Should().Contain("Exercice 2");
        studentHtml.Should().Contain("Second exercice visible.");
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
    public void RenderToHtml_AppliesPresetColorsFontAndMargin()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Fiche Thème"),
            Blocks: new List<Block> { new ParagraphBlock("Contenu") }
        );
        var preset = new StylePreset
        {
            Id = "test",
            Name = "Test",
            PrimaryColor = "#123456",
            SecondaryColor = "#654321",
            FontFamily = "Georgia, serif",
            MarginMm = 12
        };

        var html = HtmlRenderer.RenderToHtml(doc, preset);

        html.Should().Contain("--primary: #123456");
        html.Should().Contain("--secondary: #654321");
        html.Should().Contain("Georgia, serif");
        html.Should().Contain("--page-margin: 12mm 12mm 12mm 12mm");
    }

    [Fact]
    public void BuildPresetCss_AppliesAccentAndRadius()
    {
        var preset = new StylePreset
        {
            Id = "test",
            Name = "Test",
            PrimaryColor = "#123456",
            SecondaryColor = "#654321",
            AccentColor = "#ABCDEF",
            CornerRadiusPx = 14
        };

        var css = HtmlRenderer.BuildPresetCss(preset);

        css.Should().Contain("--accent: #ABCDEF");
        css.Should().Contain("--radius: 14px");
    }

    [Fact]
    public void BuildPresetCss_AccentFallsBackToSecondaryWhenEmpty()
    {
        var preset = new StylePreset
        {
            Id = "test",
            Name = "Test",
            PrimaryColor = "#123456",
            SecondaryColor = "#654321",
            AccentColor = ""
        };

        var css = HtmlRenderer.BuildPresetCss(preset);

        css.Should().Contain("--accent: #654321");
    }

    [Fact]
    public void BuildPresetCss_BandLayout_UsesGradientHeaderWithWhiteTitle()
    {
        var preset = new StylePreset { Id = "t", Name = "T", HeaderLayout = StylePreset.HeaderBand };

        var css = HtmlRenderer.BuildPresetCss(preset);

        css.Should().Contain("linear-gradient(135deg");
        css.Should().Contain(".doc-title");
        css.Should().NotContain("border-bottom: 3px double");
    }

    [Fact]
    public void BuildPresetCss_CenteredLayout_UsesDoubleRuleAndCenteredHeader()
    {
        var preset = new StylePreset { Id = "t", Name = "T", HeaderLayout = StylePreset.HeaderCentered };

        var css = HtmlRenderer.BuildPresetCss(preset);

        css.Should().Contain("text-align: center");
        css.Should().Contain("border-bottom: 3px double");
    }

    [Fact]
    public void BuildPresetCss_MinimalLayout_AddsAccentDotAndCleanTables()
    {
        var preset = new StylePreset { Id = "t", Name = "T", HeaderLayout = StylePreset.HeaderMinimal };

        var css = HtmlRenderer.BuildPresetCss(preset);

        css.Should().Contain(".doc-title::before");
        css.Should().Contain("tbody tr:nth-child(even) td { background: transparent; }");
    }

    [Fact]
    public void BuildPresetCss_UnknownLayout_FallsBackToRule()
    {
        var preset = new StylePreset { Id = "t", Name = "T", HeaderLayout = "inconnu" };

        var css = HtmlRenderer.BuildPresetCss(preset);

        css.Should().Contain("filet bicolore");
        css.Should().NotContain("linear-gradient(120deg");
    }

    [Fact]
    public void BuildPresetCss_PerSideMargins_AppliedAsShorthand()
    {
        var preset = new StylePreset
        {
            Id = "t",
            Name = "T",
            MarginMm = 20,
            MarginBottomMm = 15,
            MarginLeftMm = 18,
            MarginRightMm = 22
        };

        var css = HtmlRenderer.BuildPresetCss(preset);

        css.Should().Contain("--page-margin: 20mm 22mm 15mm 18mm");
    }

    [Fact]
    public void BuildPresetCss_OutputSurvivesCssSanitizer()
    {
        var css = HtmlRenderer.BuildPresetCss(StylePreset.Modern);

        var sanitized = CssSanitizer.SanitizeCss(css);

        // Aucune ligne du thème intégré ne doit être retirée par le sanitiseur.
        sanitized.Should().Contain(".doc-title");
        sanitized.Should().Contain(".callout");
        sanitized.Should().Contain(".document-header");
    }

    [Fact]
    public void RenderToHtml_NullPreset_FallsBackToModernDefaults()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Fiche Défaut"),
            Blocks: new List<Block> { new ParagraphBlock("Contenu") }
        );

        var html = HtmlRenderer.RenderToHtml(doc);

        html.Should().Contain("--primary: #1E3A8A");
        html.Should().Contain("--page-margin: 20mm");
    }

    [Fact]
    public void BuildPresetCss_AppendsCustomCssAfterBase()
    {
        var preset = new StylePreset
        {
            Id = "custom",
            Name = "Custom",
            PrimaryColor = "#123456",
            SecondaryColor = "#654321",
            FontFamily = "Georgia",
            CustomCss = ".doc-title { text-transform: uppercase; }"
        };

        var css = HtmlRenderer.BuildPresetCss(preset);

        css.Should().Contain("--primary: #123456");
        css.Should().Contain(".doc-title { text-transform: uppercase; }");
        css.IndexOf(".doc-title { text-transform: uppercase; }", StringComparison.Ordinal)
            .Should().BeGreaterThan(css.IndexOf("--primary:", StringComparison.Ordinal));
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

    [Fact]
    public void RenderToFullHtml_ArabicDocument_DeclaresArabicLangAndRtlDirection()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("درس في مادة الرياضيات", ClassLevel: "الخامس ابتدائي", Subject: "الرياضيات"),
            Blocks: new List<Block> { new ParagraphBlock("نص تجريبي للدرس") }
        );

        var fullHtml = HtmlRenderer.RenderToFullHtml(doc);

        fullHtml.Should().Contain("<html lang=\"ar\" dir=\"rtl\">");
    }

    [Fact]
    public void RenderToFullHtml_IncludesPedagogicalCalloutStyles()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Fiche avec blocs modulaires", ClassLevel: "CM2", Subject: "Français"),
            Blocks: new List<Block>
            {
                new CalloutBoxBlock("memo", new List<Block> { new ParagraphBlock("Règle d'or") }),
                new CalloutBoxBlock("vocabulaire", new List<Block> { new ParagraphBlock("Définition clé") }),
                new CalloutBoxBlock("prolongement", new List<Block> { new ParagraphBlock("Pour aller plus loin") })
            }
        );

        var html = HtmlRenderer.RenderToFullHtml(doc);

        html.Should().Contain(".callout-memo");
        html.Should().Contain(".callout-vocabulaire");
        html.Should().Contain(".callout-prolongement");
        html.Should().Contain("<div class=\"callout callout-memo\">");
        html.Should().Contain("<div class=\"callout callout-vocabulaire\">");
        html.Should().Contain("<div class=\"callout callout-prolongement\">");
    }
}
