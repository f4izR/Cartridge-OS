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

    /// <summary>Opens/closes the power menu (Turn Off System / Restart System / Exit to Desktop / Shut
    /// Down Cartridge OS) — Xbox Start button, previously unbound.</summary>
    Power,

    /// <summary>Both triggers (LT+RT) held together — freezes/unfreezes the stick-driven mouse cursor, so a
    /// worn stick's drift can't push the cursor around while D-Pad-only navigation is being used instead.</summary>
    ToggleCursorLock,
}
