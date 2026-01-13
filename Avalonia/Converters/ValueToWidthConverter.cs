using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaUI.Converters;

public class ValueToWidthConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 &&
            values[0] is double value &&
            values[1] is double maxWidth &&
            !double.IsNaN(maxWidth) &&
            !double.IsInfinity(maxWidth))
        {
            return Math.Max(0, value * maxWidth);
        }

        return 0d;
    }
}
