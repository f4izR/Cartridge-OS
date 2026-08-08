namespace CartridgeOS.Launcher.Input;

/// <summary>
/// A modal window that temporarily takes over gamepad routing while it's open (e.g. ArtworkCropWindow),
/// so its input doesn't also reach the launcher window underneath. Set via App.SetModalGamepadTarget —
/// while one is active, App routes ActionPressed/RightStickMoved there exclusively instead of the launcher.
/// </summary>
public interface IGamepadInputTarget
{
    void HandleAction(GamepadAction action);
    void HandleRightStick(float x, float y);
}
