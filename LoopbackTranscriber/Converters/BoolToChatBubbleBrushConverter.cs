using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LoopbackTranscriber.Converters;

public sealed class BoolToChatBubbleBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SelfBrush = new(Color.FromRgb(0x2F, 0x6F, 0xED));
    private static readonly SolidColorBrush OtherBrush = new(Color.FromRgb(0x3A, 0x3A, 0x3F));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? SelfBrush : OtherBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
