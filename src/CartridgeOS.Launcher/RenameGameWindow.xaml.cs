using System.Windows;
using System.Windows.Input;
using CartridgeOS.Launcher.Input;

namespace CartridgeOS.Launcher;

/// <summary>Small modal text-entry dialog for renaming a game — same borderless-panel chrome as
/// PowerMenuWindow, same ShowDialog/DialogResult/Result-property shape as ArtworkCropWindow.</summary>
public partial class RenameGameWindow : Window, IGamepadInputTarget
{
    /// <summary>The typed title, trimmed — only meaningful when this dialog closed with DialogResult == true.</summary>
    public string NewTitle => TitleBox.Text.Trim();

    public RenameGameWindow(string currentTitle)
    {
        InitializeComponent();
        TitleBox.Text = currentTitle;

        Loaded += (_, _) =>
        {
            // Same modal-gamepad-target handoff as ArtworkCropWindow/PowerMenuWindow (see
            // App.SetModalGamepadTarget) — without it, D-Pad/Confirm would fall through to the
            // launcher window underneath instead of this dialog.
            ((App)Application.Current).SetModalGamepadTarget(this);
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
        Closed += (_, _) => ((App)Application.Current).SetModalGamepadTarget(null);
    }

    // Renaming is keyboard-only (there's no on-screen keyboard in this app), so the gamepad's only
    // real job here is Confirm/Back — same reduced action set as PowerMenuWindow's HandleAction.
    public void HandleAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.Confirm: Accept(); break;
            case GamepadAction.Back: DialogResult = false; break;
        }
    }

    public void HandleRightStick(float x, float y) { } // ponytail: no cursor needed here, same call as PowerMenuWindow/ArtworkCropWindow's reduced dialogs

    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Accept();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
