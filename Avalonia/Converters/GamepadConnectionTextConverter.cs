using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaUI.Converters;

public class GamepadConnectionTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // trate nulos explicitamente
        if (value is not bool connected)
            return "Aguardando controle físico";

        return connected
            ? "Controle físico conectado"
            : "Aguardando controle físico";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
