using System.Globalization;
using System.Windows.Data;

namespace LoopbackTranscriber.Converters;

public sealed class WidthFractionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || width <= 0 || double.IsNaN(width) || double.IsInfinity(width))
        {
            return double.PositiveInfinity;
        }

        var fraction = 0.7;
        if (parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            fraction = parsed;
        }

        return width * fraction;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
