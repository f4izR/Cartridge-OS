using System.Globalization;
using System.Windows.Data;

namespace CartridgeOS.Launcher.Converters;

/// <summary>Binds a RadioButton's IsChecked to one value of an enum-typed property.</summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Enum.Parse(targetType, parameter!.ToString()!) : Binding.DoNothing;
}
