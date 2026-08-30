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

        presets.Should().HaveCount(6);
        presets.Select(p => p.Id).Should().Contain(new[] { "modern", "classic", "minimal", "academic", "playful", "dyslexie" });
    }

    [Fact]
    public void BuiltInPresets_HaveDistinctHeaderLayouts()
    {
        // Chaque préréglage est une direction artistique réellement différente.
        var service = new StylePresetService();
        var presets = service.GetPresets();

        presets.First(p => p.Id == "modern").HeaderLayout.Should().Be(StylePreset.HeaderRule);
        presets.First(p => p.Id == "classic").HeaderLayout.Should().Be(StylePreset.HeaderCentered);
        presets.First(p => p.Id == "minimal").HeaderLayout.Should().Be(StylePreset.HeaderMinimal);
        presets.First(p => p.Id == "academic").HeaderLayout.Should().Be(StylePreset.HeaderCentered);
        presets.First(p => p.Id == "playful").HeaderLayout.Should().Be(StylePreset.HeaderBand);
        presets.First(p => p.Id == "dyslexie").HeaderLayout.Should().Be(StylePreset.HeaderRule);

        presets.Select(p => p.AccentColor).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AddCustomPreset_MakesPresetResolvable()
    {
        var service = new StylePresetService();
        var custom = new StylePreset
        {
            Id = "custom",
            Name = "Personnalisé",
            PrimaryColor = "#123456",
            SecondaryColor = "#654321",
            AccentColor = "#ABCDEF",
            HeaderLayout = StylePreset.HeaderBand
        };

        service.AddCustomPreset(custom);
        var resolved = service.GetPreset("custom");

        resolved.Id.Should().Be("custom");
        resolved.PrimaryColor.Should().Be("#123456");
        resolved.HeaderLayout.Should().Be(StylePreset.HeaderBand);
    }

    [Fact]
    public void GenerateCss_IncludesPresetVariablesAndSanitizedCustomCss()
    {
        var service = new StylePresetService();
        var css = service.GenerateCss("classic");

        css.Should().Contain("--primary-color: #1E3A5F");
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
