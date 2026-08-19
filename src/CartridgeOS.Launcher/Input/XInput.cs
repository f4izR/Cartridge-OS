using System.Runtime.InteropServices;

namespace CartridgeOS.Launcher.Input;

[Flags]
public enum GamepadButton : ushort
{
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
    Guide = 0x0400, // Xbox guide / PS button — only visible via XInputGetStateEx, not the public XInputGetState
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputGamepad
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX;
    public short sThumbLY;
    public short sThumbRX;
    public short sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputState
{
    public uint dwPacketNumber;
    public XInputGamepad Gamepad;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputBatteryInformation
{
    public byte BatteryType;
    public byte BatteryLevel;
}

internal static class XInput
{
    // Deadzone values recommended by Microsoft for each thumbstick.
    // ponytail: both bumped above Microsoft's stock values (7849 / 8689) — GamepadWatcher folds the left
    // stick into the same D-Pad bits (ToDirectionBits), so a worn/drifting left stick that never quite
    // recenters holds a phantom direction "pressed" and fights real D-Pad input; the right stick drives the
    // mouse cursor directly (App.OnRightStickMoved), so its drift instead creeps the cursor. Bump further
    // (max 32767) if a specific pad still misbehaves; a real fix would expose this as a per-controller
    // calibration setting instead of one fixed constant for every pad.
    public const short LeftThumbDeadzone = 12000;
    public const short RightThumbDeadzone = 13000;

    private const byte BatteryDevTypeGamepad = 0;
    private const byte BatteryTypeDisconnected = 0x00;

    [DllImport("xinput1_4.dll")]
    public static extern int XInputGetState(int dwUserIndex, out XInputState pState);

    // Undocumented (exported by ordinal, not by name) — same shape as XInputGetState but its wButtons
    // also carries the guide/home button bit (0x0400), which the public XInputGetState masks out.
    // Widely relied on by other launchers/emulators for exactly this; falls back to XInputGetState
    // (still connection-checkable, just without the guide bit) if the ordinal isn't exported.
    [DllImport("xinput1_4.dll", EntryPoint = "#100")]
    private static extern int XInputGetStateEx(int dwUserIndex, out XInputState pState);

    public static int XInputGetStateWithGuide(int dwUserIndex, out XInputState pState)
    {
        try { return XInputGetStateEx(dwUserIndex, out pState); }
        catch (EntryPointNotFoundException) { return XInputGetState(dwUserIndex, out pState); }
    }

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetBatteryInformation(int dwUserIndex, byte devType, out XInputBatteryInformation pBatteryInformation);

    // ponytail: XInput only reports 4 coarse levels (Empty/Low/Medium/Full), not an exact percentage —
    // bucketed to roughly match what each level represents, not a real reading.
    public static int? GetBatteryPercent(int dwUserIndex)
    {
        if (XInputGetBatteryInformation(dwUserIndex, BatteryDevTypeGamepad, out var info) != 0) return null;
        if (info.BatteryType == BatteryTypeDisconnected) return null;

        return info.BatteryLevel switch
        {
            0 => 10,  // Empty
            1 => 35,  // Low
            2 => 65,  // Medium
            3 => 100, // Full
            _ => null,
        };
    }
}
