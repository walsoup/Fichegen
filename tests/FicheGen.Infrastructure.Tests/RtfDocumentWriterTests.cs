using FicheGen.Core.Documents;
using FicheGen.Infrastructure.Export;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class RtfDocumentWriterTests
{
    [Fact]
    public void ExportRtfString_EscapesFrenchCharactersAndIncludesFontColorTables()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Fiche Évaluation & Fractions"),
            Blocks: new List<Block>
            {
                new HeadingBlock(1, "Éléments clés"),
                new ParagraphBlock("L'élève a réussi son évaluation avec succès.")
            }
        );

        var writer = new RtfDocumentWriter();
        var rtf = writer.ExportRtfString(doc);

        rtf.Should().StartWith(@"{\rtf1");
        rtf.Should().Contain(@"\fonttbl");
        rtf.Should().Contain(@"\colortbl");
        rtf.Should().Contain(@"\u201?"); // É -> \u201?
        rtf.Should().Contain(@"\u233?"); // é -> \u233?
    }

    [Fact]
    public void ExportRtfBytes_ProducesNonEmptyByteArray()
    {
        var doc = new GeneratedDocument(
            Metadata: new DocumentMetadata("Test RTF"),
            Blocks: new List<Block> { new ParagraphBlock("Contenu RTF") }
        );

        var writer = new RtfDocumentWriter();
        var bytes = writer.ExportRtfBytes(doc);

        bytes.Should().NotBeEmpty();
    }
}
