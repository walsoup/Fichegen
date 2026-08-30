using System;
using FicheGen.App.Converters;
using FicheGen.App.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class StatusSeverityConverterTests
{
    [Theory]
    [InlineData(StatusSeverity.Info, InfoBarSeverity.Informational)]
    [InlineData(StatusSeverity.Success, InfoBarSeverity.Success)]
    [InlineData(StatusSeverity.Warning, InfoBarSeverity.Warning)]
    [InlineData(StatusSeverity.Error, InfoBarSeverity.Error)]
    public void Convert_StatusSeverity_ReturnsExpectedInfoBarSeverity(StatusSeverity input, InfoBarSeverity expected)
    {
        var converter = new StatusSeverityToInfoBarSeverityConverter();
        var result = converter.Convert(input, typeof(InfoBarSeverity), null!, "fr-FR");

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(InfoBarSeverity.Informational, StatusSeverity.Info)]
    [InlineData(InfoBarSeverity.Success, StatusSeverity.Success)]
    [InlineData(InfoBarSeverity.Warning, StatusSeverity.Warning)]
    [InlineData(InfoBarSeverity.Error, StatusSeverity.Error)]
    public void ConvertBack_InfoBarSeverity_ReturnsExpectedStatusSeverity(InfoBarSeverity input, StatusSeverity expected)
    {
        var converter = new StatusSeverityToInfoBarSeverityConverter();
        var result = converter.ConvertBack(input, typeof(StatusSeverity), null!, "fr-FR");

        result.Should().Be(expected);
    }
}
