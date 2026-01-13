using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using AvaloniaUI.ViewModels;

namespace AvaloniaUI.Converters;

/// <summary>
/// Lookup helper: given a collection of InputStatus and a name, returns the value (or 0).
/// </summary>
public class InputValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string name || string.IsNullOrWhiteSpace(name))
            return 0d;

        switch (value)
        {
            case IEnumerable<InputStatus> list:
                return list.FirstOrDefault(i => name.Equals(i.Name, StringComparison.OrdinalIgnoreCase))?.Value ?? 0d;
            case IReadOnlyDictionary<string, double> dict:
                return dict.TryGetValue(name, out var v) ? v : 0d;
            default:
                return 0d;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>
/// Returns an opacity/intensity level for a given input name.
/// Digital: 1 when pressed, otherwise BaseLevel.
/// Analog: BaseLevel..1 scaled by absolute value.
/// </summary>
public class InputLevelConverter : IValueConverter
{
    private const double BaseLevel = 0.18;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string param || string.IsNullOrWhiteSpace(param))
            return BaseLevel;

        var parts = param.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0];
        var mode = parts.Length > 1 ? parts[1] : "digital";

        double val = 0.0;
        switch (value)
        {
            case IEnumerable<InputStatus> list:
                val = list.FirstOrDefault(i => name.Equals(i.Name, StringComparison.OrdinalIgnoreCase))?.Value ?? 0.0;
                break;
            case IReadOnlyDictionary<string, double> dict:
                val = dict.TryGetValue(name, out var v) ? v : 0.0;
                break;
        }

        var abs = Math.Clamp(Math.Abs(val), 0.0, 1.0);

        var isAnalog = string.Equals(mode, "analog", StringComparison.OrdinalIgnoreCase);
        if (isAnalog)
            return BaseLevel + abs * (1.0 - BaseLevel);

        return val >= 0.5 ? 1.0 : BaseLevel;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
