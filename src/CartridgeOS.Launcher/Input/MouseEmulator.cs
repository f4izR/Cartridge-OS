using System.Runtime.InteropServices;

namespace CartridgeOS.Launcher.Input;

/// <summary>
/// Moves the real system cursor from right-stick input and clicks via the right trigger,
/// so the app is fully drivable without a physical mouse.
/// </summary>
/// <remarks>
/// ponytail: primary monitor only, no multi-monitor virtual-desktop clamping — add if that's a real complaint.
/// </remarks>
public sealed class MouseEmulator
{
    private const double MaxPixelsPerTick = 18; // tuned for the ~30Hz poll rate in GamepadWatcher

    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, nint extraInfo);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    /// <summary>stickX: right is positive, matches screen X. stickY: up is positive, but screen Y grows downward, so it's inverted.</summary>
    public void Move(float stickX, float stickY)
    {
        if (stickX == 0f && stickY == 0f) return;
        if (!GetCursorPos(out var pos)) return;

        var (width, height) = GetPrimaryScreenSize();
        int newX = Math.Clamp(pos.X + (int)(stickX * MaxPixelsPerTick), 0, width - 1);
        int newY = Math.Clamp(pos.Y - (int)(stickY * MaxPixelsPerTick), 0, height - 1);

        SetCursorPos(newX, newY);
    }

    public void SetLeftButtonDown(bool down) =>
        mouse_event(down ? MouseEventFLeftDown : MouseEventFLeftUp, 0, 0, 0, 0);

    public static (int X, int Y) GetCursorPosition()
    {
        GetCursorPos(out var pos);
        return (pos.X, pos.Y);
    }

    public static void SetCursorPosition(int x, int y) => SetCursorPos(x, y);

    public static (int Width, int Height) GetPrimaryScreenSize() =>
        (GetSystemMetrics(SmCxScreen), GetSystemMetrics(SmCyScreen));
}
