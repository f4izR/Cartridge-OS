using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CartridgeOS.Launcher.Input;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher;

public partial class OverlayWindow : Window, IGamepadInputTarget
{
    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 24;
            Top = workArea.Bottom - ActualHeight - 24;

            // Take over gamepad routing while the overlay is open (see App.SetModalGamepadTarget) —
            // otherwise only the hardcoded Menu-toggle reaches it and every other button (D-pad/Confirm)
            // falls through to the launcher window underneath instead of the overlay's own buttons.
            ((App)Application.Current).SetModalGamepadTarget(this);
            ReturnButton.Focus();
        };
        Closed += (_, _) => ((App)Application.Current).SetModalGamepadTarget(null);
    }

    public void HandleAction(GamepadAction action)
    {
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
