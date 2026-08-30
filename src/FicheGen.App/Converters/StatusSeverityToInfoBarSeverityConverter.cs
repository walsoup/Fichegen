using System;
using FicheGen.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace FicheGen.App.Converters;

public sealed class StatusSeverityToInfoBarSeverityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        string language)
    {
        if (value is StatusSeverity severity)
        {
            return severity switch
            {
                StatusSeverity.Success => InfoBarSeverity.Success,
                StatusSeverity.Warning => InfoBarSeverity.Warning,
                StatusSeverity.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            };
        }

        return InfoBarSeverity.Informational;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        string language)
    {
        if (value is InfoBarSeverity severity)
        {
            return severity switch
            {
                InfoBarSeverity.Success => StatusSeverity.Success,
                InfoBarSeverity.Warning => StatusSeverity.Warning,
                InfoBarSeverity.Error => StatusSeverity.Error,
                _ => StatusSeverity.Info
            };
        }

        return StatusSeverity.Info;
    }
}
