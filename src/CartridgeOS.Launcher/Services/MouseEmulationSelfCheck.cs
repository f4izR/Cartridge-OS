using CartridgeOS.Launcher.Input;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-mouse-emulation`.
/// Exits 0 on pass, 1 on fail. Verifies the deadzone/normalization math directly (pure,
/// no hardware needed), then briefly moves the real system cursor to confirm the P/Invoke
/// path works end to end — restores the original cursor position afterward.
/// </summary>
public static class MouseEmulationSelfCheck
{
    public static bool Run()
    {
        if (GamepadWatcher.ApplyDeadzone(0, XInput.RightThumbDeadzone) != 0f) return false;
        if (GamepadWatcher.ApplyDeadzone((short)(XInput.RightThumbDeadzone - 100), XInput.RightThumbDeadzone) != 0f) return false;

        float full = GamepadWatcher.ApplyDeadzone(32767, XInput.RightThumbDeadzone);
        if (Math.Abs(full - 1f) > 0.01f) return false;

        float negFull = GamepadWatcher.ApplyDeadzone(-32767, XInput.RightThumbDeadzone);
        if (Math.Abs(negFull + 1f) > 0.02f) return false;

        var (originalX, originalY) = MouseEmulator.GetCursorPosition();
        try
        {
            var mouse = new MouseEmulator();

            mouse.Move(0f, 0f);
            if (MouseEmulator.GetCursorPosition() != (originalX, originalY)) return false; // zero input must not move the cursor

            mouse.Move(-1f, 0f); // push fully left, away from a right edge the cursor might already be at
            var (afterX, _) = MouseEmulator.GetCursorPosition();
            bool atLeftEdge = originalX <= 1;
            if (afterX >= originalX && !atLeftEdge) return false;

            return true;
        }
        finally
        {
            MouseEmulator.SetCursorPosition(originalX, originalY); // don't leave the user's cursor displaced
        }
    }
}
