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
