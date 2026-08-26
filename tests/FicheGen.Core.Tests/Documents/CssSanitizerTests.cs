using FicheGen.Core.Documents;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Documents;

public class CssSanitizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void SanitizeCss_NullOrEmpty_ReturnsEmptyString(string? input, string expected)
    {
        var result = CssSanitizer.SanitizeCss(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void SanitizeCss_ValidRules_Preserved()
    {
        var validCss = @"body { font-family: sans-serif; color: #333; }
h1 { font-size: 24px; margin-bottom: 10px; }
.card { background-color: #fff; border-radius: 8px; }";

        var sanitized = CssSanitizer.SanitizeCss(validCss);

        sanitized.Should().Contain("body { font-family: sans-serif; color: #333; }");
        sanitized.Should().Contain("h1 { font-size: 24px; margin-bottom: 10px; }");
        sanitized.Should().Contain(".card { background-color: #fff; border-radius: 8px; }");
    }

    [Fact]
    public void SanitizeCss_DangerousConstructs_FilteredOut()
    {
        var maliciousCss = @"body { color: red; }
@import url('https://evil.com/style.css');
h1 { background: url('javascript:alert(1)'); }
div { behavior: url(script.htc); }
p { position: fixed; top: 0; }
a { position: absolute; }
.safe { font-weight: bold; }
</style><script>alert('xss')</script>";

        var sanitized = CssSanitizer.SanitizeCss(maliciousCss);

        sanitized.Should().NotContain("@import");
        sanitized.Should().NotContain("javascript:");
        sanitized.Should().NotContain("behavior:");
        sanitized.Should().NotContain("position: fixed");
        sanitized.Should().NotContain("position: absolute");
        sanitized.Should().NotContain("<script>");
        sanitized.Should().NotContain("</style>");
        sanitized.Should().Contain("body { color: red; }");
        sanitized.Should().Contain(".safe { font-weight: bold; }");
    }
}
