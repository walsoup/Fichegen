using FicheGen.Core.Toc;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Toc;

public class TocAndOffsetTests
{
    [Fact]
    public void TocParser_Tier1_DotLeaders_ParsesEntries()
    {
        var text = @"
SOMMAIRE
Leçon 1 : Les nombres entiers ......... 5
Leçon 2 : L'addition et la soustraction ......... 12
Leçon 3 : La multiplication ......... 20
Leçon 4 : La division ......... 30
Leçon 5 : Les fractions ......... 42
Leçon 6 : Les décimaux ......... 55
";

        var entries = TocParser.ParseToc(text);

        entries.Should().HaveCount(6);
        entries[0].Title.Should().Be("Leçon 1 : Les nombres entiers");
        entries[0].PrintedPage.Should().Be(5);
        entries[4].Title.Should().Be("Leçon 5 : Les fractions");
        entries[4].PrintedPage.Should().Be(42);
    }

    [Fact]
    public void TocParser_Tier2_ColumnSeparated_ParsesEntries()
    {
        var text = @"
Unités d'apprentissage
Introduction au calcul       8
Les angles et géométrie       16
Résolution de problèmes       25
Mesures de longueurs       35
Périmètres et aires       48
";

        var entries = TocParser.ParseToc(text);

        entries.Should().HaveCount(5);
        entries[0].Title.Should().Be("Introduction au calcul");
        entries[0].PrintedPage.Should().Be(8);
        entries[4].Title.Should().Be("Périmètres et aires");
        entries[4].PrintedPage.Should().Be(48);
    }

    [Fact]
    public void TocParser_Tier3_LeadingPage_ParsesEntries()
    {
        var text = @"
10 – Calcul mental
18 – Problèmes additifs
28 – Fractions simples
38 – Solides et patrons
50 – Organisation de données
";

        var entries = TocParser.ParseToc(text);

        entries.Should().HaveCount(5);
        entries[0].Title.Should().Be("Calcul mental");
        entries[0].PrintedPage.Should().Be(10);
        entries[2].Title.Should().Be("Fractions simples");
        entries[2].PrintedPage.Should().Be(28);
    }

    [Fact]
    public void TocParser_Tier1_UnicodeEllipsis_ParsesEntries()
    {
        // PDF extraction often yields '…' (U+2026) instead of ASCII dots.
        var text = @"
Leçon 1 : Les nombres entiers … 5
Leçon 2 : L'addition … 12
Leçon 3 : La multiplication … 20
Leçon 4 : La division … 30
Leçon 5 : Les fractions … 42
";

        var entries = TocParser.ParseToc(text);

        entries.Should().HaveCount(5);
        entries[0].PrintedPage.Should().Be(5);
        entries[4].Title.Should().Be("Leçon 5 : Les fractions");
    }

    [Fact]
    public void TocParser_FrenchFiches_ParsesEntries()
    {
        var text = @"
Sommaire
Fiche n° 01   L'air, une source d'énergie 7
Fiche n° 02   La combustion 11
Fiche n° 03   La transformation des matières 14
Fiche n° 04   Le système nerveux 17
Fiche n° 05   L'alimentation et la santé 20
";

        var entries = TocParser.ParseToc(text);

        entries.Should().HaveCount(5);
        entries[0].Title.Should().Contain("L'air, une source d'énergie");
        entries[0].PrintedPage.Should().Be(7);
        entries[4].Title.Should().Contain("L'alimentation et la santé");
        entries[4].PrintedPage.Should().Be(20);
    }

    [Fact]
    public void TocParser_MultiEntryLines_SplitsAndParses()
    {
        var text = @"
Fiche n° 01 L'air, source d'énergie 7 Fiche n° 02 La combustion 11
Fiche n° 03 Les leviers 14 Fiche n° 04 L'adolescence 18
";

        var entries = TocParser.ParseToc(text);

        entries.Should().HaveCount(4);
        entries[0].Title.Should().Contain("L'air, source d'énergie");
        entries[0].PrintedPage.Should().Be(7);
        entries[1].Title.Should().Contain("La combustion");
        entries[1].PrintedPage.Should().Be(11);
        entries[2].Title.Should().Contain("Les leviers");
        entries[2].PrintedPage.Should().Be(14);
        entries[3].Title.Should().Contain("L'adolescence");
        entries[3].PrintedPage.Should().Be(18);
    }

    [Fact]
    public void LevelDetector_DetectsLevelsCorrectly()
    {
        LevelDetector.DetectLevel("GUIDE_ABC_en_SCIENCE 6E.pdf").Should().Be("6e");
        LevelDetector.DetectLevel("GUIDE_ABC_en_SCIENCE CM1.pdf").Should().Be("CM1");
        LevelDetector.DetectLevel("GUIDE_ABC_en_SCIENCE CE2.pdf").Should().Be("CE2");
        LevelDetector.DetectLevel("GUIDE_ABC_en_SCIENCE CP.pdf").Should().Be("CP");
        LevelDetector.DetectLevel(@"C:\Guides\CM2\maths.pdf").Should().Be("CM2");
        LevelDetector.DetectLevel("Manuel-5eme-Histoire.pdf").Should().Be("5e");
    }

    [Fact]
    public void LevelDetector_GeneratesExpectedDropdownLabels()
    {
        LevelDetector.GenerateDefaultDropdownLabel("GUIDE_ABC_en_SCIENCE 6E.pdf")
            .Should().Be("Guide-6e-ABC en SCIENCE");
        LevelDetector.GenerateDefaultDropdownLabel("GUIDE_ABC_en_SCIENCE CM1.pdf")
            .Should().Be("Guide-CM1-ABC en SCIENCE");
    }

    [Fact]
    public void TocParser_TabSeparatedColumns_ParsesEntries()
    {
        var text = "Introduction\t10\nCalcul mental\t25\nGéométrie\t42\n";
        var entries = TocParser.ParseToc(text);

        entries.Should().HaveCount(3);
        entries[0].Title.Should().Be("Introduction");
        entries[0].PrintedPage.Should().Be(10);
        entries[1].Title.Should().Be("Calcul mental");
        entries[1].PrintedPage.Should().Be(25);
        entries[2].Title.Should().Be("Géométrie");
        entries[2].PrintedPage.Should().Be(42);
    }

    [Fact]
    public void ParentDocumentItem_LessonsSummary_HasCorrectEncoding()
    {
        var item = new ParentDocumentItem(
            "test.pdf",
            "test.pdf",
            "Guide Test",
            "guide",
            "CM2",
            string.Empty,
            new[] { new ToCEntry("Leçon 1", 5, 5), new ToCEntry("Leçon 2", 10, 10) });

        item.LessonsSummary.Should().Be("2 leçons");
        item.LessonsSummary.Should().Contain("\u00E7");
    }

    [Fact]
    public void TocParser_ProseSentencesWithPageNumbers_AreRejected()
    {
        var garbageText = @"
Évitez de vous toucher les yeux, le nez et la bouche.       5
voir mal au coeur 6. avoir mal au ventre 12. s'evanouir       1
avoir une blessure 7. une auscultation       2
un medecin 8. suer       3
tousser 9. avoir un mal de crane       4
avoir des sifflements 10. avoir mal aux epaules       5
vomir       11
Liquide       5
On trouve l'eau sous 3 états différents.       1
Demander aux élèves de réfléchir à la réponse.       1
";
        var entries = TocParser.ParseToc(garbageText);
        entries.Should().BeEmpty();
    }

    [Fact]
    public void PageOffsetDetector_CalculatesCorrectOffsetAndConfidence()
    {
        var mockSource = new TestWordSource(pageCount: 30, printedPageOffset: 10);

        var result = PageOffsetDetector.DetectOffset(mockSource);

        result.Offset.Should().Be(10);
        result.Confidence.Should().BeGreaterThan(0.5);
        result.LowConfidenceWarning.Should().BeFalse();
    }

    private class TestWordSource : IPageWordSource
    {
        public int PageCount { get; }
        private readonly int _printedPageOffset;

        public TestWordSource(int pageCount, int printedPageOffset)
        {
            PageCount = pageCount;
            _printedPageOffset = printedPageOffset;
        }

        public IReadOnlyList<PageWord> GetWords(int physicalPage)
        {
            var printedPage = physicalPage - _printedPageOffset;
            if (printedPage <= 0) return Array.Empty<PageWord>();

            return new List<PageWord>
            {
                new PageWord($"Page {printedPage}", Bottom: 50, Top: 60, Height: 10, PageWidth: 600, PageHeight: 800), // Bottom < 0.12 * 800 (96)
                new PageWord("Content text", Bottom: 400, Top: 410, Height: 10, PageWidth: 600, PageHeight: 800)
            };
        }
    }
}
