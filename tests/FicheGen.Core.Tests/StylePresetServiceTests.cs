using FicheGen.Core.Documents;
using FicheGen.Core.Services;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests;

public sealed class StylePresetServiceTests
{
    [Fact]
    public void GetPresets_ReturnsBuiltInThemes()
    {
        var service = new StylePresetService();
        var presets = service.GetPresets();

        presets.Should().HaveCount(5);
        presets.Select(p => p.Id).Should().Contain(new[] { "modern", "classic", "minimal", "academic", "playful" });
    }

    [Fact]
    public void GenerateCss_IncludesPresetVariablesAndSanitizedCustomCss()
    {
        var service = new StylePresetService();
        var css = service.GenerateCss("classic");

        css.Should().Contain("--primary-color: #1E293B");
        css.Should().Contain("Georgia");
    }

    [Fact]
    public void CssSanitizer_StripsDangerousTokens()
    {
        var rawCss = """
            h1 { color: red; }
            @import url('malicious.css');
            div { background: url('http://evil.com/bg.png'); }
            p { behavior: url(script.htc); }
            body { position: fixed; }
            h2 { color: blue; }
            """;

        var sanitized = CssSanitizer.SanitizeCss(rawCss);

        sanitized.Should().Contain("h1 { color: red; }");
        sanitized.Should().Contain("h2 { color: blue; }");
        sanitized.Should().NotContain("@import");
        sanitized.Should().NotContain("url(");
        sanitized.Should().NotContain("behavior:");
        sanitized.Should().NotContain("position: fixed");
    }
}
