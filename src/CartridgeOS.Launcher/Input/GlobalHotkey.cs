using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CartridgeOS.Launcher.Input;

/// <summary>
/// System-wide keyboard hotkey via Win32 RegisterHotKey — fires even when this app's window has
/// no focus, which is the point: toggling the overlay while a separately-launched game process
/// owns the foreground. Raw P/Invoke, same approach as XInput.cs/MouseEmulator.cs elsewhere here.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x4357; // arbitrary, only needs to be unique within this process

    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkO = 0x4F;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;

    public event Action? Pressed;

    /// <summary>
    /// Works with a window that's never shown (e.g. a hidden core window kept alive for the app's
    /// whole session) — EnsureHandle forces native handle creation without requiring Show().
    /// </summary>
    public GlobalHotkey(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("Window has no native handle yet.");
        _source.AddHook(WndProc);
        RegisterHotKey(hwnd, HotkeyId, ModControl | ModShift, VkO); // Ctrl+Shift+O
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
    }
}
