using System.Globalization;
using System.Windows.Data;

namespace CartridgeOS.Launcher.Converters;

/// <summary>True when both bound values are equal — used to drive a selection-highlight DataTrigger from
/// two independent bindings (an item and some "currently selected item" property) instead of relying on
/// a Selector's own SelectedItem/IsSelected state, which kept fighting a two-way binding to that same
/// property elsewhere (see RecentlyPlayedView's RecentRowStyle).</summary>
public sealed class EqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
