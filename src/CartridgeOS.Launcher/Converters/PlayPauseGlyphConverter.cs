using System.Globalization;
using System.Windows.Data;

namespace CartridgeOS.Launcher.Converters;

public sealed class PlayPauseGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "⏸" : "▶";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
