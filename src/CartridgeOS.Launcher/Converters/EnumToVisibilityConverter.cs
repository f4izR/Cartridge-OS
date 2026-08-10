using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CartridgeOS.Launcher.Converters;

/// <summary>Visible only when the bound enum equals ConverterParameter — same comparison as EnumToBooleanConverter, for switching screens by Visibility instead of a RadioButton's IsChecked.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString() ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
