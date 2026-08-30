using System;
using FicheGen.App.Services;
using FluentAssertions;
using Windows.UI;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class AccentColorServiceTests
{
    private static double RelativeLuminance(Color c)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;

        r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double ContrastRatio(Color c1, Color c2)
    {
        var l1 = RelativeLuminance(c1);
        var l2 = RelativeLuminance(c2);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    [Theory]
    [InlineData(0x25, 0x63, 0xEB)] // Bleu PROFstudio (#2563EB)
    [InlineData(0x7C, 0x3A, 0xED)] // Violet encre (#7C3AED)
    [InlineData(0x05, 0x96, 0x69)] // Vert forêt (#059669)
    [InlineData(0xEA, 0x58, 0x0C)] // Orange ardoise (#EA580C)
    [InlineData(0xDB, 0x27, 0x77)] // Framboise (#DB2777)
    [InlineData(0x0D, 0x94, 0x88)] // Sarcelle (#0D9488)
    public void GetContrastForeground_AllShippedPaletteColors_MeetsOrExceedsWcagContrast(byte r, byte g, byte b)
    {
        var bg = Color.FromArgb(255, r, g, b);
        var fg = AccentColorService.GetContrastForeground(bg);

        var ratio = ContrastRatio(bg, fg);
        ratio.Should().BeGreaterThanOrEqualTo(4.5, $"Contrast ratio for color RGB({r},{g},{b}) with foreground {fg} should meet WCAG AA (4.5:1)");
    }
}
