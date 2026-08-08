using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CartridgeOS.Launcher.Input;

namespace CartridgeOS.Launcher;

/// <summary>
/// Lets the user choose which part of a source image becomes a game's tile artwork, instead of
/// silently center-cropping when the source's aspect ratio doesn't match the tile's. Standard
/// pan/zoom "cover" behavior: the image can never be zoomed out past fully covering the crop
/// viewport, so there's never an empty gap at an edge.
/// </summary>
public partial class ArtworkCropWindow : Window, IGamepadInputTarget
{
    // Matches the box-art convention already used elsewhere in this app (Steam's library_600x900.jpg,
    // SteamGridDB/TheGamesDB boxart) — 2:3 portrait, close enough to the actual tile artwork slot.
    private const double ViewportWidth = 300;
    private const double ViewportHeight = 450;

    // Right stick pans directly (no on-screen cursor here, unlike its usual mouse-emulation role);
    // D-pad up/down zooms since GamepadWatcher already fires those as edge-triggered actions with
    // repeat-while-held, so holding a direction zooms continuously for free.
    private const double StickPanPixelsPerTick = 6;
    private const double DPadZoomStepFactor = 1.1;

    private readonly BitmapImage _source;
    private Point _dragStart;
    private bool _dragging;

    /// <summary>Path to the cropped PNG this dialog wrote, set only when closed via "Use This Crop".</summary>
    public string? ResultPath { get; private set; }

    public ArtworkCropWindow(string sourceImagePath)
    {
        InitializeComponent();

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad; // don't hold the source file open while the dialog is up
        bitmap.UriSource = new Uri(sourceImagePath);
        bitmap.EndInit();
        bitmap.Freeze();
        _source = bitmap;
        PreviewImage.Source = _source;

        double fillScale = Math.Max(ViewportWidth / _source.PixelWidth, ViewportHeight / _source.PixelHeight);
        ZoomSlider.Minimum = fillScale;
        ZoomSlider.Maximum = fillScale * 3;
        ZoomSlider.Value = fillScale;

        ApplyScale(fillScale);
        ImageTranslate.X = (ViewportWidth - _source.PixelWidth * fillScale) / 2;
        ImageTranslate.Y = (ViewportHeight - _source.PixelHeight * fillScale) / 2;

        // Take over gamepad routing while this dialog is open (see App.SetModalGamepadTarget) so a
        // Confirm/Back press here doesn't also reach the launcher window underneath.
        Loaded += (_, _) => ((App)Application.Current).SetModalGamepadTarget(this);
        Closed += (_, _) => ((App)Application.Current).SetModalGamepadTarget(null);
    }

    public void HandleAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.Confirm: UseCrop(); break;
            case GamepadAction.Back: DialogResult = false; break;
            case GamepadAction.NavigateUp: ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value * DPadZoomStepFactor); break;
            case GamepadAction.NavigateDown: ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value / DPadZoomStepFactor); break;
        }
    }

    public void HandleRightStick(float x, float y)
    {
        ImageTranslate.X += x * StickPanPixelsPerTick;
        ImageTranslate.Y -= y * StickPanPixelsPerTick; // GamepadWatcher already reports +y = up; screen Y grows downward
        ClampTranslate();
    }

    private void ApplyScale(double scale)
    {
        ImageScale.ScaleX = scale;
        ImageScale.ScaleY = scale;
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyScale(e.NewValue);
        ClampTranslate();
    }

    private void ClampTranslate()
    {
        double scale = ZoomSlider.Value;
        double scaledWidth = _source.PixelWidth * scale;
        double scaledHeight = _source.PixelHeight * scale;
        ImageTranslate.X = Math.Clamp(ImageTranslate.X, ViewportWidth - scaledWidth, 0);
        ImageTranslate.Y = Math.Clamp(ImageTranslate.Y, ViewportHeight - scaledHeight, 0);
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(this);
        ((UIElement)sender).CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var current = e.GetPosition(this);
        ImageTranslate.X += current.X - _dragStart.X;
        ImageTranslate.Y += current.Y - _dragStart.Y;
        _dragStart = current;
        ClampTranslate();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void UseCrop_Click(object sender, RoutedEventArgs e) => UseCrop();

    private void UseCrop()
    {
        double scale = ZoomSlider.Value;
        int x = (int)Math.Round(-ImageTranslate.X / scale);
        int y = (int)Math.Round(-ImageTranslate.Y / scale);
        int w = (int)Math.Round(ViewportWidth / scale);
        int h = (int)Math.Round(ViewportHeight / scale);

        // Clamp — rounding can push the rect a pixel past the source's actual bounds.
        w = Math.Min(w, _source.PixelWidth);
        h = Math.Min(h, _source.PixelHeight);
        x = Math.Clamp(x, 0, _source.PixelWidth - w);
        y = Math.Clamp(y, 0, _source.PixelHeight - h);

        var cropped = new CroppedBitmap(_source, new Int32Rect(x, y, w, h));

        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CartridgeOS", "ArtworkCache", "custom");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{Guid.NewGuid():N}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        ResultPath = path;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

