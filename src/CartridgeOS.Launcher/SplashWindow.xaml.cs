using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace CartridgeOS.Launcher;

/// <summary>
/// Steam-style boot splash: the logo mark traces itself in (outer shell, then inner diamond),
/// glow builds while it draws, a solid fill lands on top, then the wordmark slides/fades in.
/// Shown for a fixed duration regardless of how long the rest of startup takes — this is a brand
/// beat, not a progress indicator, so it doesn't try to track real load state (ponytail: add a
/// progress-driven variant only if a slow first-run scan actually needs one).
/// </summary>
public partial class SplashWindow : Window
{
    private static readonly string WhooshPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Splash", "whoosh.mp3");

    private readonly MediaPlayer _whoosh = new();

    public SplashWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => BeginAnimation();
        Closed += (_, _) => _whoosh.Close();
    }

    /// <summary>Raised once the reveal animation finishes (splash has been showing ~2.6s at that point).</summary>
    public event Action? AnimationCompleted;

    private void BeginAnimation()
    {
        PlayWhoosh();

        SetTraceDash(ShellTrace, GeometryLength(ShellTrace.Data));
        SetTraceDash(ShellGlow, GeometryLength(ShellGlow.Data));
        SetTraceDash(InnerLine1, LineLength(InnerLine1));
        SetTraceDash(InnerLine2, LineLength(InnerLine2));
        SetTraceDash(InnerDiamond, PolygonLength(InnerDiamond));

        var sb = new Storyboard();

        AddDoubleAnimation(sb, LogoRoot, "Opacity", 0, 1, 0, 150);
        AddDoubleAnimation(sb, LogoScale, ScaleTransform.ScaleXProperty, 0.85, 1.04, 0, 900, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });
        AddDoubleAnimation(sb, LogoScale, ScaleTransform.ScaleYProperty, 0.85, 1.04, 0, 900, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });
        AddDoubleAnimation(sb, LogoScale, ScaleTransform.ScaleXProperty, 1.04, 1.0, 900, 300);
        AddDoubleAnimation(sb, LogoScale, ScaleTransform.ScaleYProperty, 1.04, 1.0, 900, 300);

        // Animated outside the Storyboard so it can drop the Effect entirely on completion — a
        // BlurEffect left attached at Radius 0 still forces off-screen shader compositing, which
        // reads as a faint permanent softness compared to no effect at all.
        var blurAnim = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(1100));
        blurAnim.Completed += (_, _) => LogoMark.Effect = null;
        LogoBlur.BeginAnimation(BlurEffect.RadiusProperty, blurAnim);

        AddDashAnimation(sb, ShellTrace, 0, 850);
        AddDashAnimation(sb, ShellGlow, 0, 850);
        AddDoubleAnimation(sb, ShellGlow, "Opacity", 0.15, 0.95, 250, 700);

        AddDashAnimation(sb, InnerLine1, 650, 400);
        AddDashAnimation(sb, InnerLine2, 700, 400);
        AddDashAnimation(sb, InnerDiamond, 800, 500);

        AddDoubleAnimation(sb, HousingFill, "Opacity", 0, 1, 1250, 500);
        AddDoubleAnimation(sb, ShellFill, "Opacity", 0, 1, 1300, 550);

        AddDoubleAnimation(sb, Wordmark, "Opacity", 0, 1, 1750, 500);
        AddDoubleAnimation(sb, WordmarkOffset, TranslateTransform.XProperty, -16, 0, 1750, 500, new QuadraticEase { EasingMode = EasingMode.EaseOut });

        sb.Completed += (_, _) => AnimationCompleted?.Invoke();
        sb.Begin(this);
    }

    private static void AddDoubleAnimation(Storyboard sb, DependencyObject target, DependencyProperty property, double from, double to, double beginMs, double durationMs, IEasingFunction? ease = null)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs)) { BeginTime = TimeSpan.FromMilliseconds(beginMs), EasingFunction = ease };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(property));
        sb.Children.Add(anim);
    }

    private static void AddDoubleAnimation(Storyboard sb, DependencyObject target, string propertyPath, double from, double to, double beginMs, double durationMs, IEasingFunction? ease = null)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs)) { BeginTime = TimeSpan.FromMilliseconds(beginMs), EasingFunction = ease };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(propertyPath));
        sb.Children.Add(anim);
    }

    // Stroke-reveal: dash array covers the whole path length so offset==length hides it entirely
    // and offset==0 draws it in full — the standard WPF stand-in for SVG's stroke-dashoffset trick.
    private void PlayWhoosh()
    {
        try
        {
            _whoosh.Open(new Uri(WhooshPath));
            _whoosh.Play();
        }
        catch (Exception)
        {
            // ponytail: a missing/corrupt sound file must never break the splash — same guard SoundService uses.
        }
    }

    private static void SetTraceDash(Shape shape, double length)
    {
        shape.StrokeDashArray = [length, length];
        shape.StrokeDashOffset = length;
    }

    private static void AddDashAnimation(Storyboard sb, Shape shape, double beginMs, double durationMs)
    {
        double length = shape.StrokeDashArray[0];
        var anim = new DoubleAnimation(length, 0, TimeSpan.FromMilliseconds(durationMs)) { BeginTime = TimeSpan.FromMilliseconds(beginMs), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
        Storyboard.SetTarget(anim, shape);
        Storyboard.SetTargetProperty(anim, new PropertyPath(Shape.StrokeDashOffsetProperty));
        sb.Children.Add(anim);
    }

    // Flattens the path to line segments and sums their lengths — WPF has no direct "path length" API.
    private static double GeometryLength(Geometry geometry)
    {
        double total = 0;
        var flattened = geometry.GetFlattenedPathGeometry(0.25, ToleranceType.Absolute);
        foreach (var figure in flattened.Figures)
        {
            var prev = figure.StartPoint;
            foreach (var segment in figure.Segments)
            {
                if (segment is not PolyLineSegment poly) continue;
                foreach (var point in poly.Points)
                {
                    total += (point - prev).Length;
                    prev = point;
                }
            }
        }
        return total;
    }

    private static double LineLength(Line line) => (new Point(line.X2, line.Y2) - new Point(line.X1, line.Y1)).Length;

    private static double PolygonLength(Polygon polygon)
    {
        double total = 0;
        for (int i = 0; i < polygon.Points.Count; i++)
            total += (polygon.Points[(i + 1) % polygon.Points.Count] - polygon.Points[i]).Length;
        return total;
    }
}
