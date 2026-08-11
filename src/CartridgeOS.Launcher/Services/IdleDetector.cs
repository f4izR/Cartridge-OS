using System.Runtime.InteropServices;

namespace CartridgeOS.Launcher.Services;

/// <summary>System-wide keyboard/mouse idle time — the same Win32 mechanism real screen savers use
/// (GetLastInputInfo), works regardless of which window/app currently has focus. Doesn't see gamepad
/// input at all (XInput/RawGameController never touch this), so gamepad activity needs to be tracked
/// separately — see App's _lastGamepadActivityUtc.</summary>
internal static class IdleDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        // Both are tick counts since boot (DWORDs) — unsigned subtraction wraps correctly even across
        // the ~49.7-day rollover, same as GetTickCount's documented usage pattern.
        uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
