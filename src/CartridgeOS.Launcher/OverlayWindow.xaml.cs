using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CartridgeOS.Launcher.Input;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher;

public partial class OverlayWindow : Window, IGamepadInputTarget
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += (_, _) =>
        {
            // Was hardcoded to the primary monitor's work area (SystemParameters.WorkArea) — on a
            // multi-monitor setup with the game running on a secondary display, the overlay popped up on
            // the primary monitor instead, nowhere near what the user was actually looking at. The
            // foreground window at the moment the overlay opens is the running game (that's how it got
            // toggled), so target its monitor instead — physical-pixel SetWindowPos via MonitorHelper
            // sidesteps needing that monitor's own DPI scale.
            var monitor = MonitorHelper.GetMonitorBounds(GetForegroundWindow());
            MonitorHelper.MoveToBottomRightOf(this, monitor, marginPx: 24);

            // Take over gamepad routing while the overlay is open (see App.SetModalGamepadTarget) —
            // otherwise only the hardcoded Menu-toggle reaches it and every other button (D-pad/Confirm)
            // falls through to the launcher window underneath instead of the overlay's own buttons.
            ((App)Application.Current).SetModalGamepadTarget(this);
            // A running fullscreen game is actively fighting this window for OS-level foreground/input
            // focus (many games re-assert SetForegroundWindow on themselves) — Focus() alone only sets
            // WPF's own logical keyboard focus, not real OS focus, so Activate() first to actually win it,
            // otherwise HandleAction's Keyboard.FocusedElement checks below can end up empty/stale even
            // though this window is visibly on top.
            Activate();
            ReturnButton.Focus();
            Debug.WriteLine($"[Overlay] Loaded: focused={Keyboard.FocusedElement}, IsActive={IsActive}");
        };
        Closed += (_, _) => ((App)Application.Current).SetModalGamepadTarget(null);

        // Keyboard equivalent, same convention as MainWindow/PowerMenuWindow's PreviewKeyDown — was
        // missing here, leaving this the one modal dialog with no keyboard fallback (mouse-only).
        PreviewKeyDown += (_, e) =>
        {
            GamepadAction? action = e.Key switch
            {
                Key.Up => GamepadAction.NavigateUp,
                Key.Down => GamepadAction.NavigateDown,
                Key.Enter or Key.Space => GamepadAction.Confirm,
                Key.Escape => GamepadAction.Back,
                _ => null,
            };
            if (!action.HasValue) return;
            HandleAction(action.Value);
            e.Handled = true;
        };
    }

    public void HandleAction(GamepadAction action)
    {
        Debug.WriteLine($"[Overlay] HandleAction({action}): focused={Keyboard.FocusedElement}, IsActive={IsActive}");
        switch (action)
        {
            case GamepadAction.Confirm: (Keyboard.FocusedElement as Button)?.Command?.Execute(null); break;
            case GamepadAction.Back or GamepadAction.Power: Close(); break; // Power (Guide/Xbox/PS button) re-closes the overlay it just opened, same as the hotkey toggle
            case GamepadAction.NavigateUp: (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up)); break;
            case GamepadAction.NavigateDown: (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down)); break;
        }
    }

    // ponytail: no stick-driven cursor over the overlay — only two buttons, D-pad+Confirm covers it.
    public void HandleRightStick(float x, float y) { }
}
