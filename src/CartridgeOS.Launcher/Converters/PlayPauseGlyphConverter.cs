using System.Globalization;
using System.Windows.Data;

namespace CartridgeOS.Launcher.Converters;

public sealed class PlayPauseGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "M0,0 H4 V14 H0 Z M8,0 H12 V14 H8 Z" : "M0,0 L0,14 L12,7 Z"; // Path.Data strings: pause / play

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
