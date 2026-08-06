using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FicheGen.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool IsInverted { get; set; }

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        string language)
    {
        var flag = value is true;

        if (IsInverted)
        {
            flag = !flag;
        }

        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            return flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        string language)
    {
        var flag = value is Visibility.Visible;
        return IsInverted ? !flag : flag;
    }
}