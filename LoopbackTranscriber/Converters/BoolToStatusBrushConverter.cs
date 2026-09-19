using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LoopbackTranscriber.Converters;

public sealed class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush RunningBrush = new(Color.FromRgb(0x3E, 0xD5, 0x8B));
    private static readonly SolidColorBrush StoppedBrush = new(Color.FromRgb(0x6B, 0x6B, 0x70));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? RunningBrush : StoppedBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
