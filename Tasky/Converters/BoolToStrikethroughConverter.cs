using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Tasky.Converters;

[ValueConversion(typeof(bool), typeof(TextDecorationCollection))]
public class BoolToStrikethroughConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? TextDecorations.Strikethrough : new TextDecorationCollection();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
