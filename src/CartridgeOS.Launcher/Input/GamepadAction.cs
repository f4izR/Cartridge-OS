namespace CartridgeOS.Launcher.Input;

/// <summary>
/// UI-level actions the launcher reacts to. Keeps MainWindow/App decoupled from which physical
/// button, controller brand, or key produced them — GamepadWatcher does that translation.
/// </summary>
public enum GamepadAction
{
    NavigateUp,
    NavigateDown,
    NavigateLeft,
    NavigateRight,
    Confirm,
    Back,
    Secondary,
    Menu,
}
