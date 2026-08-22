using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CartridgeOS.Launcher.Converters;

/// <summary>Turns a 0..1 progress fraction into a stroked circular-arc Geometry, starting at 12 o'clock and
/// sweeping clockwise — drives the Home screen's Play-button countdown ring (see HomeView.xaml,
/// MainViewModel.HomeCarouselProgress). ConverterParameter is the square Path's own Width/Height in DIPs
/// (not the radius) — the geometry centers itself at size/2, matching how WPF centers an Ellipse's stroke
/// on its own boundary, so the arc lines up with the plain Ellipse "track" drawn underneath it instead of
/// sitting a couple pixels off from it.</summary>
public sealed class ProgressToArcGeometryConverter : IValueConverter
{
    private const double StrokeThickness = 3; // must match the Path/Ellipse StrokeThickness in HomeView.xaml

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double progress = value is double d ? Math.Clamp(d, 0.0, 1.0) : 0.0;
        double size = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sz) ? sz : 56;
        if (progress <= 0.0) return Geometry.Empty;

        double center = size / 2;
        double radius = center - StrokeThickness / 2;

        // A single ArcSegment can't express a full 360° sweep (start and end point would coincide) — clamp
        // just under it so "about to switch" still renders as a visually-complete ring instead of vanishing.
        double clamped = Math.Min(progress, 0.9995);
        double radians = (-90.0 + clamped * 360.0) * Math.PI / 180.0;

        var start = new Point(center, center - radius);
        var end = new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, clamped > 0.5, SweepDirection.Clockwise, isStroked: true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
