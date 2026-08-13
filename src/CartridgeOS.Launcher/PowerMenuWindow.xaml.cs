using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CartridgeOS.Launcher.Input;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher;

public partial class PowerMenuWindow : Window, IGamepadInputTarget
{
    public PowerMenuWindow(PowerMenuViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
            Top = workArea.Top + (workArea.Height - ActualHeight) / 2;

            // Same modal-gamepad-target pattern as ArtworkCropWindow/OverlayWindow (see App.SetModalGamepadTarget) —
            // without this, D-Pad/Confirm would fall through to whatever's underneath instead of this menu.
            ((App)Application.Current).SetModalGamepadTarget(this);
            ExitToDesktopButton.Focus(); // the harmless option gets both first position and initial focus
        };
        Closed += (_, _) => ((App)Application.Current).SetModalGamepadTarget(null);

        // Keyboard equivalent of gamepad nav/Confirm/Back — same convention as MainWindow's own
        // PreviewKeyDown, routed through the same HandleAction the controller uses instead of a separate
        // per-button shortcut scheme.
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
        switch (action)
        {
            case GamepadAction.Confirm: (Keyboard.FocusedElement as Button)?.Command?.Execute(null); break;
            case GamepadAction.Back or GamepadAction.Power: Close(); break; // Start re-closes the menu it just opened, same as the overlay's Menu toggle
            case GamepadAction.NavigateUp: (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up)); break;
            case GamepadAction.NavigateDown: (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down)); break;
        }
    }

    // ponytail: no stick-driven cursor here — four buttons, D-pad+Confirm covers it (same call as OverlayWindow).
    public void HandleRightStick(float x, float y) { }
}
