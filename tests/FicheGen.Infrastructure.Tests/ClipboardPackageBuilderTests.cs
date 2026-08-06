using System.Text.RegularExpressions;
using FicheGen.Infrastructure.Export;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class ClipboardPackageBuilderTests
{
    [Fact]
    public void FormatCfHtml_GeneratesCorrectByteOffsetHeaders()
    {
        var htmlFragment = "<h1>Titre de la fiche</h1><p>Contenu HTML de test.</p>";

        var cfHtml = ClipboardPackageBuilder.FormatCfHtml(htmlFragment);

        cfHtml.Should().Contain("Version:1.0");
        cfHtml.Should().Contain("StartHTML:");
        cfHtml.Should().Contain("EndHTML:");
        cfHtml.Should().Contain("StartFragment:");
        cfHtml.Should().Contain("EndFragment:");

        // Extract byte offset headers
        var startHtml = int.Parse(Regex.Match(cfHtml, @"StartHTML:(\d+)").Groups[1].Value);
        var endHtml = int.Parse(Regex.Match(cfHtml, @"EndHTML:(\d+)").Groups[1].Value);
        var startFragment = int.Parse(Regex.Match(cfHtml, @"StartFragment:(\d+)").Groups[1].Value);
        var endFragment = int.Parse(Regex.Match(cfHtml, @"EndFragment:(\d+)").Groups[1].Value);

        startHtml.Should().BeLessThan(startFragment);
        startFragment.Should().BeLessThan(endFragment);
        endFragment.Should().BeLessThanOrEqualTo(endHtml);
    }
}
