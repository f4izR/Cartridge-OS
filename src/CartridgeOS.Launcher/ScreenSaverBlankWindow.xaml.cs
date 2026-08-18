using System.Windows;
using System.Windows.Input;

namespace CartridgeOS.Launcher;

/// <summary>
/// Plain black window for every non-primary monitor while the screen saver is showing — the slideshow/
/// clock/music only ever plays on the primary monitor (ScreenSaverWindow); other monitors just go dark,
/// same as how Windows' own lock screen only shows its full UI on one monitor and blacks out the rest.
/// Any input here dismisses the whole screen saver (see App.ShowScreenSaver), not just this window.
/// </summary>
public partial class ScreenSaverBlankWindow : Window
{
    private bool _dismissed;
    private Point? _lastMousePosition;

    public event Action? Dismissed;

    public ScreenSaverBlankWindow()
    {
        InitializeComponent();

        PreviewKeyDown += (_, _) => Dismiss();
        PreviewMouseDown += (_, _) => Dismiss();
        PreviewMouseMove += OnPreviewMouseMove;
    }

    /// <summary>Only counts as activity past a small pixel threshold — same reasoning as ScreenSaverWindow's
    /// own guard, otherwise mouse jitter dismisses this the instant it appears.</summary>
    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_lastMousePosition is not { } last) { _lastMousePosition = position; return; }
        if ((position - last).Length > 8) Dismiss();
    }

    private void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;
        Dismissed?.Invoke();
    }
}
