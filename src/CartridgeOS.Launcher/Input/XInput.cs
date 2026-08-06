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
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
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

internal static class XInput
{
    // Deadzone values recommended by Microsoft for each thumbstick.
    public const short LeftThumbDeadzone = 7849;
    public const short RightThumbDeadzone = 8689;

    [DllImport("xinput1_4.dll")]
    public static extern int XInputGetState(int dwUserIndex, out XInputState pState);
}
