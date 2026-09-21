using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CartridgeOS.Launcher.Input;

namespace CartridgeOS.Launcher;

/// <summary>
/// Fullscreen "launching" splash — see App.LaunchGame. Deliberately a plain Window with no ViewModel;
/// it lives for a few seconds and has exactly two pieces of state (title, artwork), not worth a binding
/// layer for.
/// </summary>
public partial class GameLaunchWindow : Window
{
    public GameLaunchWindow(string title, ImageSource? initialArtwork)
    {
        InitializeComponent();
        TitleText.Text = title;
        ArtworkImage.Source = initialArtwork;

        Loaded += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).EnsureHandle();
            MonitorHelper.CoverMonitor(this, MonitorHelper.GetMonitorBounds(hwnd));
            StartAnimations();
        };
    }

    /// <summary>Swaps in the full-resolution artwork once it's decoded — the window opens immediately with
    /// whatever's already in memory (the ~200px tile thumbnail, visibly soft at fullscreen size) rather
    /// than waiting on a fresh decode, then upgrades in place.</summary>
    public void SetArtwork(ImageSource artwork) => ArtworkImage.Source = artwork;

    /// <summary>Landscape hero/custom background: letterboxed over a blurred fill, no zoom — cropping and
    /// scaling it up is what cost resolution with the portrait boxart.</summary>
    public void SetLandscapeBackground(ImageSource sharp, ImageSource? blurred)
    {
        BlurredImage.Source = blurred;
        ArtworkImage.Stretch = Stretch.Uniform;
        ArtworkImage.Source = sharp;
        // No slow zoom-in (it upscales the picture) — just a short settle from slightly enlarged to true
        // size as it fades in, so the swap from black isn't a hard cut.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var settle = new DoubleAnimation(1.05, 1.0, TimeSpan.FromMilliseconds(800)) { EasingFunction = ease };
        ArtworkScale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
        ArtworkScale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450));
        ArtworkImage.BeginAnimation(OpacityProperty, fade);
        BlurredImage.BeginAnimation(OpacityProperty, fade);
    }

    private void StartAnimations()
    {
        // Entrance: whole splash fades up from black, then the title rises into place and fades in a beat
        // later — instead of the window just appearing fully formed.
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var riseDelay = TimeSpan.FromMilliseconds(250);
        TextShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(650)) { BeginTime = riseDelay, EasingFunction = ease });
        TextPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(650)) { BeginTime = riseDelay, EasingFunction = ease });

        // Slow one-way "Ken Burns" zoom — the window's lifetime is short (a few seconds) so this never
        // needs to loop or reverse, just keep drifting in for as long as the splash stays up.
        var zoom = new DoubleAnimation(1.0, 1.08, TimeSpan.FromSeconds(6))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        ArtworkScale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        ArtworkScale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);

        var pulse = new DoubleAnimation(1.0, 0.45, TimeSpan.FromSeconds(0.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase(),
        };
        StatusText.BeginAnimation(OpacityProperty, pulse);
    }

    /// <summary>Fades to opaque black, then closes. Awaited by App.DismissLaunchSplashAsync so the window
    /// doesn't disappear mid-fade if something else tries to tear it down concurrently.</summary>
    public Task FadeOutAndCloseAsync()
    {
        var tcs = new TaskCompletionSource();
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320));
        fade.Completed += (_, _) =>
        {
            Close();
            tcs.TrySetResult();
        };
        FadeOverlay.BeginAnimation(OpacityProperty, fade);
        return tcs.Task;
    }
}
