using Windows.Gaming.Input;

namespace CartridgeOS.Launcher.Input;

/// <summary>
/// Fallback for controllers XInput doesn't recognize (e.g. PlayStation DualShock/DualSense),
/// via the DirectInput-backed Windows.Gaming.Input.RawGameController API. Shapes readings into
/// the same XInputGamepad struct so GamepadWatcher's polling/repeat/deadzone logic is reused as-is.
/// </summary>
internal static class RawGameControllerSource
{
    // Sony's USB vendor ID — DualShock/DualSense report under this regardless of product ID.
    private const ushort SonyVendorId = 0x054C;

    private static bool[] _buttons = [];
    private static GameControllerSwitchPosition[] _switches = [];
    private static double[] _axes = [];
    private static RawGameController? _lastPad;

    public static bool TryGetState(out XInputGamepad gamepad, out ControllerKind kind)
    {
        gamepad = default;
        kind = ControllerKind.Generic;
        var pad = RawGameController.RawGameControllers.FirstOrDefault();
        _lastPad = pad;
        if (pad is null) return false;

        kind = pad.HardwareVendorId == SonyVendorId ? ControllerKind.PlayStation : ControllerKind.Generic;

        if (_buttons.Length != pad.ButtonCount) _buttons = new bool[pad.ButtonCount];
        if (_switches.Length != pad.SwitchCount) _switches = new GameControllerSwitchPosition[pad.SwitchCount];
        if (_axes.Length != pad.AxisCount) _axes = new double[pad.AxisCount];
        pad.GetCurrentReading(_buttons, _switches, _axes);

        ushort bits = 0;
        // ponytail: button/axis order follows the common HID gamepad convention (Square/Cross/Circle/Triangle,
        // Share/Options, LX/LY/RX/RY/LT/RT) that DualShock/DualSense report under RawGameController. Not
        // guaranteed by the API for every pad — recalibrate here if a specific controller maps wrong.
        if (Held(0)) bits |= (ushort)GamepadButton.X;
        if (Held(1)) bits |= (ushort)GamepadButton.A;
        if (Held(2)) bits |= (ushort)GamepadButton.B;
        if (Held(3)) bits |= (ushort)GamepadButton.Y;
        if (Held(4)) bits |= (ushort)GamepadButton.LeftShoulder;
        if (Held(5)) bits |= (ushort)GamepadButton.RightShoulder;
        if (Held(8)) bits |= (ushort)GamepadButton.Back;
        if (Held(9)) bits |= (ushort)GamepadButton.Start;
        // ponytail: index 12 is the PS/guide button on DualShock4/DualSense's HID report (after
        // Share/Options/L3/R3) under RawGameController — pad-dependent, recalibrate if it maps wrong.
        if (Held(12)) bits |= (ushort)GamepadButton.Guide;

        if (_switches.Length > 0)
        {
            var dpad = _switches[0];
            if (dpad is GameControllerSwitchPosition.Up or GameControllerSwitchPosition.UpLeft or GameControllerSwitchPosition.UpRight) bits |= (ushort)GamepadButton.DPadUp;
            if (dpad is GameControllerSwitchPosition.Down or GameControllerSwitchPosition.DownLeft or GameControllerSwitchPosition.DownRight) bits |= (ushort)GamepadButton.DPadDown;
            if (dpad is GameControllerSwitchPosition.Left or GameControllerSwitchPosition.UpLeft or GameControllerSwitchPosition.DownLeft) bits |= (ushort)GamepadButton.DPadLeft;
            if (dpad is GameControllerSwitchPosition.Right or GameControllerSwitchPosition.UpRight or GameControllerSwitchPosition.DownRight) bits |= (ushort)GamepadButton.DPadRight;
        }

        gamepad.wButtons = bits;
        gamepad.sThumbLX = ToShort(0, invert: false);
        gamepad.sThumbLY = ToShort(1, invert: true);
        gamepad.sThumbRX = ToShort(2, invert: false);
        // ponytail: confirmed live on a DualShock 4 — this pad's RawGameController report puts L2/R2
        // between RX and RY (axes 3,4), with RY last (axis 5), not the LX,LY,RX,RY,L2,R2 order assumed
        // above for buttons. Reading axis 3 as RY (the old code) meant every poll fed a trigger's
        // rest-at-0 reading through the inverted stick formula, which pins to full deflection — that's
        // exactly the "cursor drifts up by itself" bug. And axis 5 (the real RY) was being read as R2,
        // so pulling the stick down was misread as a right-click. Swap fixes both.
        gamepad.bLeftTrigger = ToByte(3);
        gamepad.bRightTrigger = ToByte(4);
        gamepad.sThumbRY = ToShort(5, invert: true);
        return true;
    }

    /// <summary>Battery percent for the most recently polled pad (from <see cref="TryGetState"/>), or null if unreported.</summary>
    public static int? GetBatteryPercent()
    {
        var report = _lastPad?.TryGetBatteryReport();
        int? remaining = report?.RemainingCapacityInMilliwattHours;
        int? full = report?.FullChargeCapacityInMilliwattHours;
        if (remaining is null || full is null || full <= 0) return null;
        return (int)Math.Clamp(100.0 * remaining.Value / full.Value, 0, 100);
    }

    private static bool Held(int index) => index < _buttons.Length && _buttons[index];

    private static short ToShort(int axisIndex, bool invert)
    {
        if (axisIndex >= _axes.Length) return 0;
        double v = _axes[axisIndex] * 2 - 1; // 0..1 -> -1..1
        if (invert) v = -v;
        return (short)Math.Clamp(v * 32767, short.MinValue, short.MaxValue);
    }

    private static byte ToByte(int axisIndex)
    {
        if (axisIndex >= _axes.Length) return 0;
        return (byte)Math.Clamp(_axes[axisIndex] * 255, 0, 255);
    }
}
