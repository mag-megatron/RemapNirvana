using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AvaloniaUI.Converters;

/// <summary>
/// Converts LX/LY (or RX/RY) values into a TranslateTransform to move the thumb indicator.
/// </summary>
public class StickOffsetConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        double x = Extract(values, 0);
        double y = Extract(values, 1);

        var radius = 14.0;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            radius = parsed;

        x = Math.Clamp(x, -1, 1) * radius;
        // SDL already reports Y increasing when the stick is pushed down.
        // Keep the sign as-is so the UI reflects the emitted direction.
        y = Math.Clamp(y, -1, 1) * radius;

        return new TranslateTransform(x, y);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;

    private static double Extract(IList<object?> values, int index)
    {
        if (index >= values.Count)
            return 0;

        return values[index] switch
        {
            double d => d,
            float f => f,
            _ => 0
        };
    }
}
