namespace CartridgeOS.Launcher.Input;

/// <summary>
/// Polls XInput controllers on a background thread and raises edge-triggered
/// navigation events, with directional repeat while a stick/dpad is held.
/// </summary>
public sealed class GamepadWatcher
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(33); // ~30Hz, plenty for UI nav
    private static readonly TimeSpan InitialRepeatDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(130);

    private const byte TriggerPressThreshold = 128; // half-pressed, out of 0-255

    private static readonly GamepadButton[] DirectionButtons =
        [GamepadButton.DPadUp, GamepadButton.DPadDown, GamepadButton.DPadLeft, GamepadButton.DPadRight];

    private readonly Dictionary<GamepadButton, DateTime> _nextRepeatAt = new();
    private ushort _previousButtons;
    private bool _rightTriggerHeld;
    private CancellationTokenSource? _cts;

    public event Action<GamepadButton>? ButtonPressed;

    /// <summary>Fired with normalized (-1..1) deadzone-filtered values whenever the right stick is off-center.</summary>
    public event Action<float, float>? RightStickMoved;

    /// <summary>Fired on press/release edges of the right trigger — used as a mouse-click button.</summary>
    public event Action<bool>? RightTriggerChanged;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (XInput.XInputGetState(0, out var state) == 0) // 0 = ERROR_SUCCESS, controller connected
            {
                Poll(state.Gamepad, DateTime.UtcNow);
            }
            else
            {
                _previousButtons = 0;
                _nextRepeatAt.Clear();
                if (_rightTriggerHeld) { _rightTriggerHeld = false; RightTriggerChanged?.Invoke(false); } // don't leave the mouse button stuck down on disconnect
            }

            try { await Task.Delay(PollInterval, token); } catch (TaskCanceledException) { }
        }
    }

    private void Poll(XInputGamepad gamepad, DateTime now)
    {
        ushort buttons = (ushort)(gamepad.wButtons | ToDirectionBits(gamepad));

        foreach (var direction in DirectionButtons)
        {
            bool held = ((GamepadButton)buttons & direction) != 0;
            bool wasHeld = ((GamepadButton)_previousButtons & direction) != 0;

            if (held && !wasHeld)
            {
                ButtonPressed?.Invoke(direction);
                _nextRepeatAt[direction] = now + InitialRepeatDelay;
            }
            else if (held && wasHeld && now >= _nextRepeatAt.GetValueOrDefault(direction))
            {
                ButtonPressed?.Invoke(direction);
                _nextRepeatAt[direction] = now + RepeatInterval;
            }
            else if (!held)
            {
                _nextRepeatAt.Remove(direction);
            }
        }

        foreach (var button in new[] { GamepadButton.A, GamepadButton.B, GamepadButton.X, GamepadButton.Y, GamepadButton.Start, GamepadButton.Back })
        {
            bool pressed = ((GamepadButton)buttons & button) != 0 && ((GamepadButton)_previousButtons & button) == 0;
            if (pressed) ButtonPressed?.Invoke(button);
        }

        _previousButtons = buttons;

        float rightX = ApplyDeadzone(gamepad.sThumbRX, XInput.RightThumbDeadzone);
        float rightY = ApplyDeadzone(gamepad.sThumbRY, XInput.RightThumbDeadzone);
        if (rightX != 0f || rightY != 0f) RightStickMoved?.Invoke(rightX, rightY);

        bool triggerHeld = gamepad.bRightTrigger > TriggerPressThreshold;
        if (triggerHeld != _rightTriggerHeld)
        {
            _rightTriggerHeld = triggerHeld;
            RightTriggerChanged?.Invoke(triggerHeld);
        }
    }

    // Folds the left thumbstick into the same dpad-direction bits so stick and dpad nav share one code path.
    private static ushort ToDirectionBits(XInputGamepad gamepad)
    {
        ushort bits = 0;
        if (gamepad.sThumbLY > XInput.LeftThumbDeadzone) bits |= (ushort)GamepadButton.DPadUp;
        if (gamepad.sThumbLY < -XInput.LeftThumbDeadzone) bits |= (ushort)GamepadButton.DPadDown;
        if (gamepad.sThumbLX < -XInput.LeftThumbDeadzone) bits |= (ushort)GamepadButton.DPadLeft;
        if (gamepad.sThumbLX > XInput.LeftThumbDeadzone) bits |= (ushort)GamepadButton.DPadRight;
        return bits;
    }

    // internal (not private) so the self-check can verify this math directly without needing real hardware.
    internal static float ApplyDeadzone(short value, short deadzone)
    {
        if (Math.Abs((int)value) < deadzone) return 0f;

        float sign = Math.Sign(value);
        float magnitude = (Math.Abs((float)value) - deadzone) / (32767f - deadzone);
        return sign * Math.Clamp(magnitude, 0f, 1f);
    }
}
