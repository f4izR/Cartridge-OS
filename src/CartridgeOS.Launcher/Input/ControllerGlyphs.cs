namespace CartridgeOS.Launcher.Input;

/// <summary>Per-brand button labels for on-screen prompts (e.g. "A" vs "✕" for Confirm).</summary>
public static class ControllerGlyphs
{
    public static string Label(ControllerKind kind, GamepadAction action) => (kind, action) switch
    {
        (ControllerKind.Xbox, GamepadAction.Confirm) => "A",
        (ControllerKind.Xbox, GamepadAction.Back) => "B",
        (ControllerKind.Xbox, GamepadAction.Secondary) => "Y",
        (ControllerKind.Xbox, GamepadAction.Menu) => "Menu", // hamburger button (was called "Start" pre–Xbox One) — opens item options
        (ControllerKind.Xbox, GamepadAction.ToggleSettings) => "View",
        (ControllerKind.Xbox, GamepadAction.ToggleSearch) => "X",
        (ControllerKind.Xbox, GamepadAction.Power) => "Xbox", // the round Guide button
        (ControllerKind.Xbox, GamepadAction.PreviousTab) => "LB",
        (ControllerKind.Xbox, GamepadAction.NextTab) => "RB",

        (ControllerKind.PlayStation, GamepadAction.Confirm) => "✕", // Cross
        (ControllerKind.PlayStation, GamepadAction.Back) => "○", // Circle
        (ControllerKind.PlayStation, GamepadAction.Secondary) => "△", // Triangle
        (ControllerKind.PlayStation, GamepadAction.Menu) => "Options", // opens item options, same slot as Xbox's Menu button
        (ControllerKind.PlayStation, GamepadAction.ToggleSettings) => "Share",
        (ControllerKind.PlayStation, GamepadAction.ToggleSearch) => "□", // Square
        (ControllerKind.PlayStation, GamepadAction.Power) => "PS", // the round PS button
        (ControllerKind.PlayStation, GamepadAction.PreviousTab) => "L1",
        (ControllerKind.PlayStation, GamepadAction.NextTab) => "R1",

        // No controller connected — label the actual keyboard key (MainWindow.xaml.cs's PreviewKeyDown
        // switch is the source of truth these must match; see keybinds.md's "Keyboard equivalents" table).
        // PreviousTab/NextTab/ToggleSearch have no keyboard shortcut at all (deliberate — see keybinds.md);
        // their badges are hidden via MainViewModel.IsControllerConnected instead of shown with a fake label.
        (ControllerKind.Keyboard, GamepadAction.Confirm) => "Enter",
        (ControllerKind.Keyboard, GamepadAction.Back) => "Esc",
        (ControllerKind.Keyboard, GamepadAction.Secondary) => "Insert",
        (ControllerKind.Keyboard, GamepadAction.Menu) => "Menu",
        (ControllerKind.Keyboard, GamepadAction.ToggleSettings) => "Tab",
        (ControllerKind.Keyboard, GamepadAction.Power) => "F4",

        (_, GamepadAction.Confirm) => "1",
        (_, GamepadAction.Back) => "2",
        (_, GamepadAction.Secondary) => "4",
        (_, GamepadAction.Menu) => "Menu",
        (_, GamepadAction.ToggleSettings) => "Select",
        (_, GamepadAction.ToggleSearch) => "3",
        (_, GamepadAction.Power) => "Guide",
        (_, GamepadAction.PreviousTab) => "LB",
        (_, GamepadAction.NextTab) => "RB",

        _ => action.ToString(), // navigation actions have no glyph, just the enum name
    };
}
