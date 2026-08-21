namespace CartridgeOS.Launcher.Input;

/// <summary>Which brand of controller is currently connected — drives button-glyph labels.</summary>
public enum ControllerKind
{
    Xbox,
    PlayStation,
    Generic,
    /// <summary>No gamepad connected — on-screen prompts should show the keyboard equivalent instead of a
    /// made-up controller glyph.</summary>
    Keyboard,
}
