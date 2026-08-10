using System.Globalization;
using System.Windows.Data;

namespace CartridgeOS.Launcher.Converters;

/// <summary>Maps a 0-100 percent value to a pixel width against a fixed track width (ConverterParameter) —
/// simpler than measuring the actual track at runtime via RelativeSource/MultiBinding for a bar whose
/// container width is already fixed in the layout.</summary>
public sealed class PercentToPixelWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double percent = value is double d ? d : 0;
        double trackWidth = parameter is string s && double.TryParse(s, out var w) ? w : 100;
        return Math.Clamp(percent / 100.0 * trackWidth, 0, trackWidth);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
