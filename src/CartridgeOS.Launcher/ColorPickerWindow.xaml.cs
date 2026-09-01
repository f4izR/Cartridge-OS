using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CartridgeOS.Launcher.Input;

namespace CartridgeOS.Launcher;

/// <summary>
/// Modal color picker — a click/drag saturation-value field plus a hue strip (the classic layout the
/// Windows "Define Custom Colors" panel used, ColorDialog's own custom-color picker), with numeric R/G/B
/// and hex entry alongside for anyone who wants an exact value instead of clicking. Same borderless-panel
/// chrome and ShowDialog/DialogResult shape as RenameGameWindow/ArtworkCropWindow — used instead of
/// System.Windows.Forms.ColorDialog (Settings > Theme's custom accent pickers) because that dialog is OS
/// chrome that can't be themed to match the rest of the app.
/// </summary>
public partial class ColorPickerWindow : Window, IGamepadInputTarget
{
    /// <summary>The chosen color as "#RRGGBB" — only meaningful when this dialog closed with DialogResult == true.</summary>
    public string SelectedColor { get; private set; } = "";

    // HSV, not RGB, is the model here — the SV square and hue strip both edit one HSV component in
    // isolation, and deriving that back out of an RGB triple on every drag tick would be both more work
    // and lossy right at black/white/gray (where hue is undefined).
    private double _hue; // 0-360
    private double _saturation; // 0-1
    private double _value; // 0-1

    // Guards every update path (SV square drag, hue strip drag, hex box, RGB boxes) against re-entering
    // itself — each one writes the other UI pieces back, which would otherwise fight/loop.
    private bool _syncing;

    public ColorPickerWindow(string initialHex, string title = "Pick a Color")
    {
        InitializeComponent();
        TitleText.Text = title;

        var initial = ParseOrDefault(initialHex);
        (_hue, _saturation, _value) = RgbToHsv(initial);
        UpdateFromHsv();

        Loaded += (_, _) =>
        {
            // Same modal-gamepad-target handoff as ArtworkCropWindow/PowerMenuWindow/RenameGameWindow (see
            // App.SetModalGamepadTarget) — without it, D-Pad/Confirm would fall through to the launcher
            // window underneath instead of this dialog.
            ((App)Application.Current).SetModalGamepadTarget(this);
        };
        Closed += (_, _) => ((App)Application.Current).SetModalGamepadTarget(null);
    }

    private Color CurrentColor => HsvToRgb(_hue, _saturation, _value);

    /// <summary>Single source of truth for repainting everything from the current HSV — call this after
    /// any change to _hue/_saturation/_value, however it happened.</summary>
    private void UpdateFromHsv()
    {
        var c = CurrentColor;
        PreviewSwatch.Background = new SolidColorBrush(c);
        HueBackground.Fill = new SolidColorBrush(HsvToRgb(_hue, 1, 1));

        SvCursor.Margin = new Thickness(_saturation * SvSquare.Width - SvCursor.Width / 2,
            (1 - _value) * SvSquare.Height - SvCursor.Height / 2, 0, 0);
        HueCursor.Margin = new Thickness(0, _hue / 360 * (HueStrip.Height - HueCursor.Height), 0, 0);

        _syncing = true;
        RedBox.Text = c.R.ToString();
        GreenBox.Text = c.G.ToString();
        BlueBox.Text = c.B.ToString();
        HexBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        _syncing = false;
    }

    private void SvSquare_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SvSquare.CaptureMouse();
        ApplySvPosition(e.GetPosition(SvSquare));
    }

    private void SvSquare_MouseMove(object sender, MouseEventArgs e)
    {
        if (!SvSquare.IsMouseCaptured) return;
        ApplySvPosition(e.GetPosition(SvSquare));
    }

    private void SvSquare_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => SvSquare.ReleaseMouseCapture();

    private void ApplySvPosition(Point p)
    {
        _saturation = Clamp01(p.X / SvSquare.Width);
        _value = 1 - Clamp01(p.Y / SvSquare.Height);
        UpdateFromHsv();
    }

    private void HueStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HueStrip.CaptureMouse();
        ApplyHuePosition(e.GetPosition(HueStrip));
    }

    private void HueStrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!HueStrip.IsMouseCaptured) return;
        ApplyHuePosition(e.GetPosition(HueStrip));
    }

    private void HueStrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => HueStrip.ReleaseMouseCapture();

    private void ApplyHuePosition(Point p)
    {
        _hue = Clamp01(p.Y / HueStrip.Height) * 360;
        UpdateFromHsv();
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e) => ApplyHexBox();
    private void HexBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) ApplyHexBox(); }

    private void ApplyHexBox()
    {
        if (_syncing) return;
        if (!TryParseColor(HexBox.Text, out var c)) return; // ponytail: invalid/partial hex is just ignored until corrected, no inline error UI

        (_hue, _saturation, _value) = RgbToHsv(c);
        UpdateFromHsv();
    }

    private void RgbBox_LostFocus(object sender, RoutedEventArgs e) => ApplyRgbBoxes();
    private void RgbBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) ApplyRgbBoxes(); }

    private void ApplyRgbBoxes()
    {
        if (_syncing) return;
        if (!byte.TryParse(RedBox.Text, out byte r) || !byte.TryParse(GreenBox.Text, out byte g) || !byte.TryParse(BlueBox.Text, out byte b))
            return; // ponytail: invalid/partial numeric entry is just ignored until corrected, no inline error UI

        (_hue, _saturation, _value) = RgbToHsv(Color.FromRgb(r, g, b));
        UpdateFromHsv();
    }

    public void HandleAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.Confirm: Accept(); break;
            case GamepadAction.Back: DialogResult = false; break;
        }
    }

    public void HandleRightStick(float x, float y) { } // ponytail: no cursor needed here, same call as PowerMenuWindow/ArtworkCropWindow's reduced dialogs

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Accept()
    {
        var c = CurrentColor;
        SelectedColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        DialogResult = true;
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static Color HsvToRgb(double h, double s, double v)
    {
        h %= 360;
        if (h < 0) h += 360;
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static (double H, double S, double V) RgbToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0.00001)
        {
            if (max == r) h = 60 * ((g - b) / delta % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;

        double s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }

    private static Color ParseOrDefault(string hex) => TryParseColor(hex, out var c) ? c : Colors.White;

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
