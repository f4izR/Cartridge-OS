using System.Windows;
using System.Windows.Media;

namespace CartridgeOS.Launcher.Services;

/// <summary>One named accent-color pair shown as a swatch in Settings > Theme.</summary>
public sealed record ThemePreset(string Name, string Accent1, string Accent2);

/// <summary>
/// Applies the user's chosen accent colors to the app's brand resources at runtime.
///
/// Every brand-related resource this touches is consumed via DynamicResource in XAML (Theme.xaml,
/// SharedStyles.xaml, and each view), not StaticResource — that distinction matters here: a Style or
/// ControlTemplate freezes any Freezable value captured through StaticResource the first time it's sealed
/// (which happens the first time a control using it is rendered), so a shared brush mutated in place would
/// stop updating the moment the first Button/tile/toggle using it actually appears on screen. DynamicResource
/// never gets baked into a sealed Setter that way — it re-resolves this dictionary on every change — so
/// Apply() can simply replace each entry outright on every call, no frozen-object bookkeeping needed.
/// </summary>
public static class ThemeService
{
    public const string DefaultAccent1 = "#51E7ED";
    public const string DefaultAccent2 = "#168DDC";

    public static readonly IReadOnlyList<ThemePreset> Presets =
    [
        new("Default Cyan", DefaultAccent1, DefaultAccent2),
        new("Violet", "#B389F9", "#6C3CE9"),
        new("Crimson", "#FF6B81", "#C81E4A"),
        new("Emerald", "#52E7A6", "#0E9C64"),
        new("Amber", "#FFC857", "#E8871E"),
        new("Magenta", "#FF7AC6", "#D6248C"),
    ];

    /// <summary>Parses and applies an accent pair, replacing every dependent resource in
    /// Application.Resources. Invalid hex strings are ignored rather than throwing — this runs on every
    /// app startup from saved settings, and a corrupted settings.json shouldn't crash it.</summary>
    public static void Apply(string accent1Hex, string accent2Hex)
    {
        if (!TryParseColor(accent1Hex, out var c1) || !TryParseColor(accent2Hex, out var c2)) return;

        var resources = Application.Current.Resources;
        var mid = Color.FromRgb((byte)((c1.R + c2.R) / 2), (byte)((c1.G + c2.G) / 2), (byte)((c1.B + c2.B) / 2));
        var hover = Lighten(c1, 0.2);
        var pressed = Darken(c1, 0.2);

        // Raw Color entries — consumed directly where a DependencyProperty needs a Color rather than a
        // Brush (DropShadowEffect.Color, a plain SolidColorBrush.Color), always via DynamicResource for
        // the same reason as the brushes below. Never inside a Storyboard's ColorAnimation.To, though —
        // BeginStoryboard.Seal() needs to freeze the whole timeline tree, which a DynamicResource inside it
        // blocks (throws "Cannot freeze this Storyboard timeline tree"); the two spots that used to animate
        // a border's color this way (LibraryView/RecentlyPlayedView selection) use a plain Setter instead.
        resources["BrandCyanColor"] = c1;
        resources["BrandBlueColor"] = mid;
        resources["BrandDeepBlueColor"] = c2;

        resources["BrandCyanBrush"] = new SolidColorBrush(c1);
        resources["BrandBlueBrush"] = new SolidColorBrush(mid);
        resources["BrandDeepBlueBrush"] = new SolidColorBrush(c2);
        resources["BrandCyanHoverBrush"] = new SolidColorBrush(hover);
        resources["BrandCyanPressedBrush"] = new SolidColorBrush(pressed);
        resources["SelectedTileBorderBrush"] = new SolidColorBrush(mid);
        resources["SelectedTileAccentBrush"] = new SolidColorBrush(c1) { Opacity = 0.08 };
        resources["FocusGlowBrush"] = new SolidColorBrush(c1) { Opacity = 0.30 };

        var existingGradient = resources["BrandGradientBrush"] as LinearGradientBrush;
        var gradient = new LinearGradientBrush
        {
            StartPoint = existingGradient?.StartPoint ?? new Point(0, 0),
            EndPoint = existingGradient?.EndPoint ?? new Point(1, 1),
        };
        gradient.GradientStops.Add(new GradientStop(c1, 0));
        gradient.GradientStops.Add(new GradientStop(c2, 1));
        resources["BrandGradientBrush"] = gradient;

        // Same hue as BrandGradientBrush, heavily darkened/muted for a large selected-state background
        // (Recently Played's hero card) where the full-brightness gradient would read as too bright.
        var existingHero = resources["HeroGradientBrush"] as LinearGradientBrush;
        var heroGradient = new LinearGradientBrush
        {
            StartPoint = existingHero?.StartPoint ?? new Point(0, 0),
            EndPoint = existingHero?.EndPoint ?? new Point(1, 1),
        };
        heroGradient.GradientStops.Add(new GradientStop(Darken(c1, 0.75), 0));
        heroGradient.GradientStops.Add(new GradientStop(Darken(c2, 0.75), 1));
        resources["HeroGradientBrush"] = heroGradient;
    }

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));

    private static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex)!;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            color = default;
            return false;
        }
    }
}
