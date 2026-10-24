using System.Globalization;
using Avalonia.Data.Converters;

namespace Tasky.Converters;

/// <summary>True when the bound string has visible text — used to hide empty badges and labels.</summary>
public class StringHasTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
