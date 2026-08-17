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

        (ControllerKind.PlayStation, GamepadAction.Confirm) => "✕", // Cross
        (ControllerKind.PlayStation, GamepadAction.Back) => "○", // Circle
        (ControllerKind.PlayStation, GamepadAction.Secondary) => "△", // Triangle
        (ControllerKind.PlayStation, GamepadAction.Menu) => "Options", // opens item options, same slot as Xbox's Menu button
        (ControllerKind.PlayStation, GamepadAction.ToggleSettings) => "Share",
        (ControllerKind.PlayStation, GamepadAction.ToggleSearch) => "□", // Square
        (ControllerKind.PlayStation, GamepadAction.Power) => "PS", // the round PS button

        (_, GamepadAction.Confirm) => "1",
        (_, GamepadAction.Back) => "2",
        (_, GamepadAction.Secondary) => "4",
        (_, GamepadAction.Menu) => "Menu",
        (_, GamepadAction.ToggleSettings) => "Select",
        (_, GamepadAction.ToggleSearch) => "3",
        (_, GamepadAction.Power) => "Guide",

        _ => action.ToString(), // navigation actions have no glyph, just the enum name
    };
}
