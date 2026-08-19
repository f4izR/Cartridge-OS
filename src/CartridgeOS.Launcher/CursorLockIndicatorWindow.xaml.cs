using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CartridgeOS.Launcher;

/// <summary>
/// Small always-on-top badge shown while the gamepad-driven mouse cursor is frozen (LT+RT combo,
/// see GamepadWatcher/App.OnGamepadAction) — otherwise there was no visible feedback that the lock
/// actually took effect, just a confirm sound. Created once and kept alive for the app's lifetime;
/// SetLocked shows/hides it rather than recreating it each toggle.
/// </summary>
public partial class CursorLockIndicatorWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    public CursorLockIndicatorWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PositionAtBottomCenter();
        // ShowActivated="False" (set in XAML) only stops WPF's own Activated/focus bookkeeping on show —
        // Windows can still hand this window OS-level foreground focus, which steals WPF's app-wide
        // Keyboard.FocusedElement away from whatever's actually driving the D-Pad (e.g. the Power menu),
        // breaking its MoveFocus/Command calls entirely. WS_EX_NOACTIVATE is the real fix: it makes the
        // window structurally unable to receive focus, not just "don't focus it right now."
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate);
        };
    }

    public void SetLocked(bool locked)
    {
        if (locked) Show();
        else Hide();
    }

    private void PositionAtBottomCenter()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Bottom - ActualHeight - 40;
    }
}
