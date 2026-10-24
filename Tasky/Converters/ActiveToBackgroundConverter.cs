using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Tasky.Converters;

/// <summary>Returns a green highlight brush when the task is active.</summary>
public class ActiveToBackgroundConverter : IValueConverter
{
    private static readonly IBrush Active   = new SolidColorBrush(Color.FromArgb(60, 34, 197, 94));  // green tint
    private static readonly IBrush Inactive = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Active : Inactive;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
