using System;
using FicheGen.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FicheGen.App.Views.Controls;

public sealed class DiffLineKindToVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        string language)
    {
        var expected = parameter?.ToString()?.Trim().ToLowerInvariant();

        var actual = value switch
        {
            DiffLineKind.Added => "added",
            DiffLineKind.Removed => "removed",
            DiffLineKind.Collapsed => "collapsed",
            DiffLineKind.Context => "context",
            string text => text.Trim().ToLowerInvariant(),
            _ => "context"
        };

        return string.Equals(actual, expected, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        string language) =>
        throw new NotSupportedException();
}