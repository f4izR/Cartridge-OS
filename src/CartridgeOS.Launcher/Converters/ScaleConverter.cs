using System.Globalization;
using System.Windows.Data;

namespace CartridgeOS.Launcher.Converters;

/// <summary>Multiplies a bound double (MainViewModel.UiScale) by a numeric ConverterParameter — used to
/// scale tile fonts/icons in LibraryView/HomeView down in step with the tile sizes themselves on a
/// smaller display, instead of shrinking the tile but leaving its text/icon at design size.</summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double scale = value is double d ? d : 1.0;
        double baseSize = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : 1.0;
        return scale * baseSize;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
