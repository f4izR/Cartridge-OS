using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CartridgeOS.Launcher.Views;

/// <summary>
/// Ambient drifting-dot background, purely decorative — originally Recently Played's, now shared with
/// any screen that wants the same look (e.g. Library). Each dot re-enters at a random X and drifts from
/// the bottom edge to the top on a loop (staggered durations/opacities/sizes so it doesn't read as a
/// mechanical repeat), then picks a new X and starts over — see StartDrift.
/// </summary>
public partial class AmbientParticleBackground : UserControl
{
    private const int ParticleCount = 150;
    private static readonly Random Rng = new();

    public AmbientParticleBackground()
    {
        InitializeComponent();
    }

    private void ParticleCanvas_Loaded(object sender, RoutedEventArgs e) => SpawnParticles();

    private void ParticleCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => SpawnParticles();

    private void SpawnParticles()
    {
        if (ParticleCanvas.ActualWidth <= 0 || ParticleCanvas.ActualHeight <= 0) return;
        ParticleCanvas.Children.Clear();

        for (int i = 0; i < ParticleCount; i++)
        {
            double size = Rng.NextDouble() * 2.5 + 1.5; // 1.5-4px
            var dot = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = Rng.NextDouble() * 0.35 + 0.1 };
            var transform = new TranslateTransform();
            dot.RenderTransform = transform;
            Canvas.SetLeft(dot, Rng.NextDouble() * ParticleCanvas.ActualWidth);
            Canvas.SetTop(dot, ParticleCanvas.ActualHeight);
            ParticleCanvas.Children.Add(dot);

            // Scatter across the full height on first paint so the screen isn't empty at launch; every
            // subsequent cycle (StartDrift) travels the full bottom-to-top distance instead.
            StartDrift(dot, transform, -Rng.NextDouble() * ParticleCanvas.ActualHeight);
        }
    }

    private void StartDrift(Ellipse dot, TranslateTransform transform, double fromY)
    {
        double duration = Rng.NextDouble() * 25 + 35; // 35-60s bottom-to-top
        var animation = new DoubleAnimation
        {
            // Explicit From, not transform.Y — once an animation completes, WPF holds the property at
            // that end value (FillBehavior.HoldEnd), so reading transform.Y here would just return the
            // previous cycle's To and produce a zero-length (invisible) animation on every restart.
            From = fromY,
            To = -(ParticleCanvas.ActualHeight + dot.Height),
            Duration = TimeSpan.FromSeconds(duration),
        };
        animation.Completed += (_, _) =>
        {
            Canvas.SetLeft(dot, Rng.NextDouble() * ParticleCanvas.ActualWidth); // re-enter at a new X each cycle
            StartDrift(dot, transform, 0);
        };
        transform.BeginAnimation(TranslateTransform.YProperty, animation);
    }
}
