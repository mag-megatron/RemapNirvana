using System;
using System.Globalization;
using Avalonia.Data.Converters;
using AvaloniaUI.Models;
using System.Linq;
using Avalonia.Controls;

namespace AvaloniaUI.Converters
{
    public class ProfileActionTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Profile profile)
                return "";

            var itemsControl = parameter as ItemsControl;
            if (itemsControl?.Items is System.Collections.IEnumerable profiles)
            {
                var first = profiles.Cast<Profile>().FirstOrDefault();
                return profile == first ? "+" : "–";
            }

            return profile.Name == "Padrão" ? "+" : "–";
        }


        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
