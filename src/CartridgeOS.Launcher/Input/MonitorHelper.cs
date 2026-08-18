using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CartridgeOS.Launcher.Input;

/// <summary>
/// Physical-pixel monitor enumeration and window placement, for windows that need to cover or target
/// a specific monitor rather than always the primary one (screen saver, in-game overlay). Positions
/// via raw SetWindowPos in physical pixels rather than WPF's Left/Top/Width/Height (device-independent
/// units) — sidesteps per-monitor DPI conversion entirely, which matters once a second monitor can have
/// a different DPI scale than the primary one.
/// </summary>
public static class MonitorHelper
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorRect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public MonitorRect Monitor;
        public MonitorRect WorkArea;
        public uint Flags;
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref MonitorRect lprcMonitor, nint dwData);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out MonitorRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;

    /// <summary>Every monitor's full bounds (not work area — the screen saver should cover taskbars too).</summary>
    public static List<MonitorRect> GetAllMonitorBounds()
    {
        var monitors = new List<MonitorRect>();
        EnumDisplayMonitors(0, 0, (nint _, nint _, ref MonitorRect rect, nint _) => { monitors.Add(rect); return true; }, 0);
        return monitors;
    }

    /// <summary>The monitor nearest the given HWND — used to place the in-game overlay on whichever monitor
    /// the running game actually is on instead of always the primary.</summary>
    public static MonitorRect GetMonitorBounds(nint hwnd)
    {
        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        return info.Monitor;
    }

    /// <summary>Resizes+repositions a window to exactly cover a monitor. Call after Show() — needs a real HWND.</summary>
    public static void CoverMonitor(Window window, MonitorRect rect)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(hwnd, nint.Zero, rect.Left, rect.Top, rect.Width, rect.Height, SwpNoZOrder | SwpShowWindow);
    }

    /// <summary>Moves (without resizing) a window's bottom-right corner to sit inset from a monitor's
    /// bottom-right corner by marginPx — reads the window's own already-rendered physical size via
    /// GetWindowRect rather than converting its WPF ActualWidth/Height, avoiding needing that monitor's
    /// DPI scale at all. Call after Show(), once the window has a real size.</summary>
    public static void MoveToBottomRightOf(Window window, MonitorRect monitor, int marginPx)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (!GetWindowRect(hwnd, out var current)) return;
        int x = monitor.Right - current.Width - marginPx;
        int y = monitor.Bottom - current.Height - marginPx;
        SetWindowPos(hwnd, nint.Zero, x, y, 0, 0, SwpNoZOrder | SwpNoSize | SwpShowWindow);
    }
}
