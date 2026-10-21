using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Tasky.Converters;

/// <summary>Returns a green highlight brush when the task is active.</summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public class ActiveToBackgroundConverter : IValueConverter
{
    private static readonly Brush Active   = new SolidColorBrush(Color.FromArgb(60, 34, 197, 94));  // green tint
    private static readonly Brush Inactive = Brushes.Transparent;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Active : Inactive;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
