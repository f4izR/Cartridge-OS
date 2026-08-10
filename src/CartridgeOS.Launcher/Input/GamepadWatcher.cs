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

    // Physical-button -> UI-action vocabulary. X and the view/select "Back" button aren't wired to
    // anything in the launcher yet, so they're left unmapped (ponytail: add an entry when they get a use).
    private static readonly Dictionary<GamepadButton, GamepadAction> ActionMap = new()
    {
        [GamepadButton.DPadUp] = GamepadAction.NavigateUp,
        [GamepadButton.DPadDown] = GamepadAction.NavigateDown,
        [GamepadButton.DPadLeft] = GamepadAction.NavigateLeft,
        [GamepadButton.DPadRight] = GamepadAction.NavigateRight,
        [GamepadButton.A] = GamepadAction.Confirm,
        [GamepadButton.B] = GamepadAction.Back,
        [GamepadButton.Y] = GamepadAction.Secondary,
        [GamepadButton.Start] = GamepadAction.Menu,
        [GamepadButton.LeftShoulder] = GamepadAction.PreviousTab,
        [GamepadButton.RightShoulder] = GamepadAction.NextTab,
    };

    private static readonly TimeSpan BatteryCheckInterval = TimeSpan.FromSeconds(10); // battery queries are a real syscall each time — not worth doing every 33ms poll

    private readonly Dictionary<GamepadButton, DateTime> _nextRepeatAt = new();
    private ushort _previousButtons;
    private bool _rightTriggerHeld;
    private DateTime _nextBatteryCheckAt = DateTime.MinValue;
    private CancellationTokenSource? _cts;

    public ControllerKind? CurrentController { get; private set; }

    public int? ControllerBatteryPercent { get; private set; }

    /// <summary>UI-level action, edge-triggered with directional repeat while a stick/dpad is held.</summary>
    public event Action<GamepadAction>? ActionPressed;

    /// <summary>Fired whenever the connected controller's brand changes, including to/from null on connect/disconnect.</summary>
    public event Action<ControllerKind?>? ControllerChanged;

    /// <summary>Fired whenever the connected controller's battery reading changes (including to/from null).</summary>
    public event Action<int?>? ControllerBatteryChanged;

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
            var now = DateTime.UtcNow;
            if (XInput.XInputGetState(0, out var state) == 0) // 0 = ERROR_SUCCESS, controller connected
            {
                SetController(ControllerKind.Xbox);
                RefreshBatteryIfDue(now, () => XInput.GetBatteryPercent(0));
                Poll(state.Gamepad, now);
            }
            else if (RawGameControllerSource.TryGetState(out var rawGamepad, out var kind)) // XInput doesn't see PlayStation pads; fall back to DirectInput-backed RawGameController
            {
                SetController(kind);
                RefreshBatteryIfDue(now, RawGameControllerSource.GetBatteryPercent);
                Poll(rawGamepad, now);
            }
            else
            {
                SetController(null);
                SetBattery(null);
                _previousButtons = 0;
                _nextRepeatAt.Clear();
                if (_rightTriggerHeld) { _rightTriggerHeld = false; RightTriggerChanged?.Invoke(false); } // don't leave the mouse button stuck down on disconnect
            }

            try { await Task.Delay(PollInterval, token); } catch (TaskCanceledException) { }
        }
    }

    private void SetController(ControllerKind? kind)
    {
        if (kind == CurrentController) return;
        CurrentController = kind;
        ControllerChanged?.Invoke(kind);
    }

    private void SetBattery(int? percent)
    {
        if (percent == ControllerBatteryPercent) return;
        ControllerBatteryPercent = percent;
        ControllerBatteryChanged?.Invoke(percent);
    }

    private void RefreshBatteryIfDue(DateTime now, Func<int?> read)
    {
        if (now < _nextBatteryCheckAt) return;
        _nextBatteryCheckAt = now + BatteryCheckInterval;
        SetBattery(read());
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
                ActionPressed?.Invoke(ActionMap[direction]);
                _nextRepeatAt[direction] = now + InitialRepeatDelay;
            }
            else if (held && wasHeld && now >= _nextRepeatAt.GetValueOrDefault(direction))
            {
                ActionPressed?.Invoke(ActionMap[direction]);
                _nextRepeatAt[direction] = now + RepeatInterval;
            }
            else if (!held)
            {
                _nextRepeatAt.Remove(direction);
            }
        }

        foreach (var (button, action) in ActionMap)
        {
            if (DirectionButtons.Contains(button)) continue; // handled above, with repeat
            bool pressed = ((GamepadButton)buttons & button) != 0 && ((GamepadButton)_previousButtons & button) == 0;
            if (pressed) ActionPressed?.Invoke(action);
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
