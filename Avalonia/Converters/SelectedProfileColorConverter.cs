using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace AvaloniaUI.Converters;

public class SelectedProfileColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush(Color.Parse("#3c8dbc"))
            : new SolidColorBrush(Color.Parse("#1e1e1e"));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
