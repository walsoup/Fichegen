using System;
using Microsoft.UI.Xaml.Data;

namespace FicheGen.App.Converters;

/// <summary>
/// Value converter that formats a value using a string format pattern.
/// Usage: Text="{Binding Value, Converter={StaticResource StringFormatConverter}, ConverterParameter='{0:HH:mm}'}"
/// </summary>
public sealed class StringFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null || parameter == null)
            return string.Empty;

        var format = parameter.ToString();
        if (format == null) return value?.ToString() ?? string.Empty;
        
        // Handle both simple {0} placeholders and .NET format strings like {0:HH:mm}
        try
        {
            return string.Format(format, value);
        }
        catch
        {
            return value?.ToString() ?? string.Empty;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
