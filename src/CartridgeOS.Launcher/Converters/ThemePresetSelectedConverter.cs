using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CartridgeOS.Launcher.Converters;

/// <summary>True when a preset's Accent1/Accent2 (values[0..1]) match the currently applied
/// ThemeAccentColor1/2 (values[2..3]) — drives the highlight ring on the active swatch in
/// Settings > Theme. Ordinal, case-insensitive: both sides are "#RRGGBB" hex strings.</summary>
public sealed class ThemePresetSelectedConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool selected = values.Length == 4
            && string.Equals(values[0] as string, values[2] as string, StringComparison.OrdinalIgnoreCase)
            && string.Equals(values[1] as string, values[3] as string, StringComparison.OrdinalIgnoreCase);

        if (targetType == typeof(Visibility)) return selected ? Visibility.Visible : Visibility.Collapsed;
        return selected;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
