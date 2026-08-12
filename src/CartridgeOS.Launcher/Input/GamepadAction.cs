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
    PreviousTab,
    NextTab,

    /// <summary>Opens the settings sidebar directly (Xbox View / PlayStation Share button) — toggles it,
    /// same as clicking the gear icon.</summary>
    ToggleSettings,

    /// <summary>Opens/closes the Library search box (Xbox X / PlayStation Square) — no-ops outside Library,
    /// same screen-scoping the mouse-driven search icon already has.</summary>
    ToggleSearch,
}
