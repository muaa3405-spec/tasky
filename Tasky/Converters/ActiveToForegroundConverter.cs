using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Tasky.Converters;

[ValueConversion(typeof(bool), typeof(Brush))]
public class ActiveToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? new SolidColorBrush(Color.FromRgb(22, 163, 74)) : new SolidColorBrush(Color.FromRgb(107, 114, 128));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
