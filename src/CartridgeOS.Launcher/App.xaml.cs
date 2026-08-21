using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CartridgeOS.Core.Data;
using CartridgeOS.Core.Ipc;
using CartridgeOS.Core.Models;
using CartridgeOS.Launcher.Input;
using CartridgeOS.Launcher.Services;
using CartridgeOS.Launcher.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;

namespace CartridgeOS.Launcher;

/// <summary>
/// The application "core": single-instance guard, tray icon, and every service that must survive
/// the launcher window being closed (gamepad/hotkey listeners, running-game tracking, overlay,
/// Discord presence). The launcher window itself is disposable — created and destroyed on demand,
/// never kept resident just to avoid a recreate. See progress.md/context.md for the full rationale.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Global\CartridgeOS.SingleInstance";
    private const string SingleInstancePipeName = "CartridgeOS.Launcher.SingleInstance";

    private static readonly Dictionary<string, Func<bool>> SelfChecks = new()
    {
        ["--self-check-artwork"] = ArtworkCacheSelfCheck.Run,
        ["--self-check-steam"] = SteamScannerSelfCheck.Run,
        ["--self-check-epic"] = EpicManifestSelfCheck.Run,
        ["--self-check-riot"] = RiotManifestSelfCheck.Run,
        ["--self-check-executable-heuristics"] = ExecutableHeuristicsSelfCheck.Run,
        ["--self-check-standalone"] = StandaloneScannerSelfCheck.Run,
        ["--self-check-sound"] = SoundServiceSelfCheck.Run,
        ["--self-check-ipc"] = PipeIpcSelfCheck.Run,
        ["--self-check-mouse-emulation"] = MouseEmulationSelfCheck.Run,
        ["--self-check-xbox"] = XboxScannerSelfCheck.Run,
    };

    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _singleInstancePipeCts;
    private TaskbarIcon? _trayIcon;
    private Window? _coreWindow;
    private GamepadWatcher? _gamepad;
    private MouseEmulator? _mouse;
    private ControllerKind? _currentController;

    /// <summary>Read by MainWindow when opening the power menu, so its Confirm/Back prompts match the
    /// controller actually connected (Xbox "A"/"B", PlayStation "✕"/"○", etc.) — see ControllerGlyphs.</summary>
    internal ControllerKind? CurrentController => _currentController;
    private IGamepadInputTarget? _modalGamepadTarget;
    private bool _cursorLocked; // toggled by LT+RT — see OnGamepadAction/OnRightStickMoved
    private GlobalHotkey? _overlayHotkey;
    private DiscordRichPresence? _discord;

    private MainWindow? _launcherWindow;
    private int? _lastSelectedGameId;

    private OverlayWindow? _overlayWindow;
    private Process? _runningGameProcess;
    private string? _runningGameTitle;
    private string? _runningGameExePath; // fallback lookup key for TryResumeRunningGame when _runningGameProcess's own handle is stale (see HandleGameProcessExitedAsync's stub-process comment)

    private DispatcherTimer? _idleTimer;
    private DateTime _lastGamepadActivityUtc = DateTime.UtcNow; // GetLastInputInfo (IdleDetector) never sees gamepad input, so this is tracked separately
    private readonly List<ScreenSaverWindow> _screenSaverWindows = [];
    private readonly List<ScreenSaverBlankWindow> _screenSaverBlankWindows = [];
    private UpdateChecker.UpdateInfo? _pendingUpdate;

    // No logging framework exists anywhere in this app — before this, an unhandled exception left zero
    // trail, just a generic Windows "stopped working" dialog. One append-only text file is enough to
    // turn a user's crash report into something debuggable.
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CartridgeOS", "crash.log");

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}\n\n");
        }
        catch
        {
            // logging must never throw during crash handling
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Best-effort: log and let the process die rather than try to keep running on corrupted state.
        DispatcherUnhandledException += (_, args) => LogCrash("DispatcherUnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash("UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { LogCrash("UnobservedTaskException", args.Exception); args.SetObserved(); };

        var selfCheck = SelfChecks.Keys.FirstOrDefault(e.Args.Contains);
        if (selfCheck is not null)
        {
            Environment.Exit(SelfChecks[selfCheck]() ? 0 : 1);
            return;
        }

        // Cross-process diagnostic (separate from --self-check-ipc, which runs its own in-process
        // server): pings whatever is actually listening on the Service's IPC pipe.
        if (e.Args.Contains("--ipc-ping"))
        {
            var response = new CartridgeOsPipeClient().SendAsync(new PipeRequest("GetGameCount")).GetAwaiter().GetResult();
            Environment.Exit(response is { Success: true } ? 0 : 1);
            return;
        }

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Another instance already owns the mutex — signal it to show itself and let this one exit.
            // No window, no tray icon, nothing gets created in this process.
            new CartridgeOsPipeClient().SendAsync(new PipeRequest("ShowLauncher"), SingleInstancePipeName).GetAwaiter().GetResult();
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        // Never shown — exists purely to give GlobalHotkey a stable native window handle that
        // outlives the launcher window being opened and closed repeatedly.
        _coreWindow = new Window { Width = 0, Height = 0, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Visibility = Visibility.Hidden };
        new WindowInteropHelper(_coreWindow).EnsureHandle();

        _gamepad = new GamepadWatcher();
        _mouse = new MouseEmulator();
        _gamepad.ActionPressed += OnGamepadAction;
        _gamepad.ControllerChanged += OnControllerChanged;
        _gamepad.ControllerBatteryChanged += OnControllerBatteryChanged;
        _gamepad.RightStickMoved += OnRightStickMoved;
        _gamepad.RightTriggerChanged += OnRightTriggerChanged;
        _gamepad.Start();

        _overlayHotkey = new GlobalHotkey(_coreWindow);
        _overlayHotkey.Pressed += ToggleOverlay;

        _discord = new DiscordRichPresence();
        _ = _discord.ConnectAsync(); // no-op if Discord isn't running

        _trayIcon = (TaskbarIcon)Resources["TrayIcon"];

        _singleInstancePipeCts = new CancellationTokenSource();
        _ = new CartridgeOsPipeServer(HandleSingleInstanceSignal, SingleInstancePipeName).RunAsync(_singleInstancePipeCts.Token);

        // System-wide, not tied to launcher-window focus — same as a real screen saver, this fires
        // regardless of what app currently has focus. See CheckIdle for the conditions that suppress it.
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick += (_, _) => CheckIdle();
        _idleTimer.Start();

        _ = CheckForUpdateAsync();

        ShowSplashThenLauncher();
    }

    // Steam-style boot splash: shows immediately, plays its fixed reveal animation, then swaps to
    // the real launcher window.
    private void ShowSplashThenLauncher()
    {
        var splash = new SplashWindow();
        splash.AnimationCompleted += () => Dispatcher.BeginInvoke(() =>
        {
            ShowLauncher();
            splash.Close();
        });
        splash.Show();
    }

    /// <summary>Fire-and-forget, once per app launch. Nudge-only (see UpdateChecker) — stores the result
    /// so ShowLauncher can apply it whenever the launcher window actually exists, since this can resolve
    /// before the splash screen finishes or while the window is closed (tray-only) later on.</summary>
    private async Task CheckForUpdateAsync()
    {
        var update = await UpdateChecker.CheckAsync();
        if (update is null) return;

        _pendingUpdate = update;
        _ = Dispatcher.BeginInvoke(() => _launcherWindow?.ShowUpdateAvailable(update.Version, update.ReleaseUrl));
    }

    private PipeResponse HandleSingleInstanceSignal(PipeRequest request)
    {
        if (request.Command != "ShowLauncher") return new PipeResponse(false, $"unknown command '{request.Command}'");
        Dispatcher.BeginInvoke(ShowLauncher);
        return new PipeResponse(true);
    }

    private void OnGamepadAction(GamepadAction action)
    {
        _lastGamepadActivityUtc = DateTime.UtcNow; // GetLastInputInfo never sees this — tracked separately for CheckIdle
        Dispatcher.BeginInvoke(() =>
        {
            // LT+RT together — freezes/unfreezes the stick-driven cursor (see OnRightStickMoved) so a
            // drifting stick can't fight D-Pad-only navigation. Live everywhere, even over a modal dialog.
            if (action == GamepadAction.ToggleCursorLock)
            {
                _cursorLocked = !_cursorLocked;
                Debug.WriteLine($"[App] ToggleCursorLock -> _cursorLocked={_cursorLocked}");
                SoundService.PlayConfirm();
                // Indicator lives in-window now (header pill / overlay footer line), not a floating
                // always-on-top badge — that used to render over games and other apps, and stealing
                // focus back to it (however briefly) was what made the lock only "take" while it was
                // the topmost window. Pushing state into whichever of our own windows is open sidesteps
                // both problems.
                _launcherWindow?.UpdateCursorLocked(_cursorLocked);
                if (_overlayWindow?.DataContext is OverlayViewModel overlayVm) overlayVm.IsCursorLocked = _cursorLocked;
                return;
            }

            // A modal dialog (e.g. ArtworkCropWindow) takes over entirely while it's open — its input must
            // never also reach the launcher window underneath (double-handling a Confirm/Back press).
            if (_modalGamepadTarget is { } target)
            {
                Debug.WriteLine($"[App] {action} -> modal target {target.GetType().Name}");
                target.HandleAction(action);
                return;
            }

            Debug.WriteLine($"[App] {action} -> launcher window (running game: {_runningGameProcess is not null})");

            // Power (the Guide/Xbox/PS button, see GamepadWatcher.ActionMap) means two things depending on
            // context: toggle the in-game overlay while a game is running (works with no launcher window open,
            // unlike the old per-window binding), or — when nothing's running — open the Power menu in the
            // launcher (its normal binding, handled by MainWindow.HandleGamepadAction like any other action).
            if (action == GamepadAction.Power && _runningGameProcess is not null) ToggleOverlay();
            else _launcherWindow?.HandleGamepadAction(action);
        });
    }

    /// <summary>Called by a modal window (e.g. ArtworkCropWindow) on open/close to take over — or release — exclusive gamepad routing.</summary>
    public void SetModalGamepadTarget(IGamepadInputTarget? target) => _modalGamepadTarget = target;

    private void OnRightStickMoved(float x, float y)
    {
        _lastGamepadActivityUtc = DateTime.UtcNow;
        // Never move the real OS cursor while some other app (most importantly a running game) has
        // focus — regardless of lock state. Before this guard, a drifting/off-center stick kept
        // nudging the cursor even while alt-tabbed away or while a fullscreen game had real input
        // focus, which is never useful and can fight the game's own input.
        if (!CartridgeOS.Launcher.MainWindow.IsForegroundWindowInThisProcess()) return; // qualified: `MainWindow` unqualified here resolves to Application.MainWindow, not the type
        if (_modalGamepadTarget is { } target) { Dispatcher.BeginInvoke(() => target.HandleRightStick(x, y)); return; }
        if (_cursorLocked) return; // stick-driven cursor suspended — use D-Pad instead (see OnGamepadAction ToggleCursorLock)
        _mouse!.Move(x, y); // pure Win32 P/Invoke, not a WPF object — safe to call straight from the poll thread, no Dispatcher needed
    }

    private void OnRightTriggerChanged(bool held)
    {
        _lastGamepadActivityUtc = DateTime.UtcNow;
        if (_modalGamepadTarget is not null) return; // avoid a stray emulated click landing on the dialog underneath while it's open
        _mouse!.SetLeftButtonDown(held);
    }

    /// <summary>Ticks every 1s (see OnStartup) — suppressed entirely while the screen saver is already
    /// showing, a game is running, or some other modal (e.g. ArtworkCropWindow) has gamepad focus.
    /// Reloads AppSettings fresh every tick rather than caching a copy — it's a tiny JSON file, and this
    /// sidesteps needing any change-notification plumbing between the Settings UI (which edits its own
    /// MainViewModel-owned AppSettings instance) and this class.</summary>
    private void CheckIdle()
    {
        var settings = SettingsStore.Load();
        if (!settings.ScreenSaverEnabled) return;
        if (_screenSaverWindows.Count > 0) return;
        if (_runningGameProcess is not null) return;
        if (_modalGamepadTarget is not null) return;

        var threshold = TimeSpan.FromMinutes(settings.ScreenSaverInactivityMinutes);
        bool idle = IdleDetector.GetIdleTime() >= threshold && DateTime.UtcNow - _lastGamepadActivityUtc >= threshold;
        if (idle) ShowScreenSaver(settings);
    }

    /// <summary>Settings → "Preview Now" — bypasses the enabled toggle and inactivity duration entirely
    /// (testing should work even while the feature is turned off), but still won't interrupt a running game.</summary>
    public void ShowScreenSaverNow()
    {
        if (_screenSaverWindows.Count > 0 || _runningGameProcess is not null) return;
        ShowScreenSaver(SettingsStore.Load());
    }

    /// <summary>The slideshow/clock/music only ever plays on the primary monitor (unchanged Maximized
    /// behavior) — a screen saver that only blanks the primary display and leaves every other monitor
    /// fully visible/interactive defeats the point, but showing the full slideshow duplicated across every
    /// monitor is more than asked for. Other monitors just go black (same as Windows' own lock screen).
    /// Dismissing any one window (input, gamepad, or the primary's own audio fade-out finishing) closes
    /// all of them together.</summary>
    private void ShowScreenSaver(AppSettings settings)
    {
        var primary = new ScreenSaverWindow(settings);
        primary.Dismissed += CloseAllScreenSavers;
        primary.Closed += (_, _) => _screenSaverWindows.Remove(primary);
        primary.Show();
        _screenSaverWindows.Add(primary);

        var monitors = MonitorHelper.GetAllMonitorBounds();
        var primaryBounds = MonitorHelper.GetMonitorBounds(new WindowInteropHelper(primary).EnsureHandle());
        foreach (var monitor in monitors)
        {
            if (monitor.Left == primaryBounds.Left && monitor.Top == primaryBounds.Top) continue;

            var blank = new ScreenSaverBlankWindow();
            blank.Dismissed += CloseAllScreenSavers;
            blank.Closed += (_, _) => _screenSaverBlankWindows.Remove(blank);
            blank.Show();
            MonitorHelper.CoverMonitor(blank, monitor);
            _screenSaverBlankWindows.Add(blank);
        }
    }

    private void CloseAllScreenSavers()
    {
        foreach (var window in _screenSaverWindows.ToArray()) window.Dismiss();
        foreach (var window in _screenSaverBlankWindows.ToArray()) window.Close();
    }

    /// <summary>Keeps the overlay's on-screen button prompt matching whatever controller is actually plugged in.</summary>
    private void OnControllerChanged(ControllerKind? kind)
    {
        // Fired from GamepadWatcher's background poll thread — every touch of a bound viewmodel property must
        // marshal to the UI thread, same reason OnGamepadAction does.
        Dispatcher.BeginInvoke(() =>
        {
            _currentController = kind;
            if (_overlayWindow?.DataContext is OverlayViewModel vm) vm.MenuButtonLabel = ControllerGlyphs.Label(kind ?? ControllerKind.Keyboard, GamepadAction.Power);
            _launcherWindow?.UpdateControllerKind(kind);
        });
    }

    private void OnControllerBatteryChanged(int? percent) => Dispatcher.BeginInvoke(() => _launcherWindow?.UpdateControllerBattery(percent));

    private void OpenLauncher_Click(object sender, RoutedEventArgs e) => ShowLauncher();

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    /// <summary>Recreates the launcher window if it was closed (restoring the last selection), or just refocuses it if it's already open.</summary>
    private void ShowLauncher()
    {
        if (_launcherWindow is null)
        {
            _launcherWindow = new MainWindow(_lastSelectedGameId);
            _launcherWindow.UpdateControllerBattery(_gamepad?.ControllerBatteryPercent);
            _launcherWindow.UpdateCursorLocked(_cursorLocked);
            _launcherWindow.UpdateControllerKind(_currentController);
            _launcherWindow.UpdateRunningGame(_runningGameTitle);
            _launcherWindow.Closed += (_, _) => OnLauncherClosed();
            this.MainWindow = _launcherWindow; // Application.MainWindow — qualified to disambiguate from the MainWindow type

            if (_pendingUpdate is { } update) _launcherWindow.ShowUpdateAvailable(update.Version, update.ReleaseUrl);
        }

        _launcherWindow.WindowState = WindowState.Maximized;
        _launcherWindow.Show();
        _launcherWindow.Activate();
    }

    /// <summary>
    /// Fires when the launcher window finishes closing — X button, Alt+F4, or a programmatic
    /// Close() all end up here (ShutdownMode="OnExplicitShutdown" means none of those quit the app).
    /// Saves the one bit of state worth restoring, then drops every reference to the closed
    /// window/viewmodel/tile graph so it's actually collectible, followed by one deliberate GC pass
    /// so the freed artwork bitmaps are reclaimed now rather than whenever the next GC happens to run.
    /// </summary>
    private void OnLauncherClosed()
    {
        _lastSelectedGameId = _launcherWindow?.CurrentSelectedGameId;
        _launcherWindow = null;

        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    // Ctrl+Shift+O or controller Start — only meaningful while a game we launched (and can therefore
    // track) is running. Lives here rather than on the launcher window, since that window may already
    // be closed while the game keeps running.
    private void ToggleOverlay()
    {
        if (_runningGameProcess is null) return;

        if (_overlayWindow is not null)
        {
            CloseOverlay();
            return;
        }

        var overlayVm = new OverlayViewModel(_runningGameTitle ?? "Game", ReturnToLauncher, QuitRunningGame, _currentController) { IsCursorLocked = _cursorLocked };
        _overlayWindow = new OverlayWindow(overlayVm);
        _overlayWindow.Closed += (_, _) => _overlayWindow = null;
        _overlayWindow.Show();

        // Topmost="True" in XAML only affects z-order relative to other already-open windows — it
        // doesn't forcibly steal the foreground game's spot. Re-toggling it after Show/Activate makes
        // Windows actually re-apply topmost z-order now, which is what gets the overlay to appear
        // without alt-tabbing away from the game first. (Won't help over true DirectX exclusive
        // fullscreen — that bypasses the desktop compositor entirely; borderless/windowed games are fine.)
        _overlayWindow.Activate();
        _overlayWindow.Topmost = false;
        _overlayWindow.Topmost = true;
    }

    private void CloseOverlay()
    {
        _overlayWindow?.Close();
        _overlayWindow = null;
    }

    private void ReturnToLauncher()
    {
        CloseOverlay();
        ShowLauncher();
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    private const int SwRestore = 9;

    /// <summary>Brings the already-running game's own window back to the foreground and minimizes the
    /// launcher out of its way — the switch-back half of "Return to Cartridge OS", which only ever went
    /// the other direction (there was no way to get back to the game once the launcher was showing again,
    /// short of alt-tabbing outside the app entirely). Returns false if there's nothing trackable to
    /// resume (no game running, or it was a Steam/Xbox shell launch — see LaunchGame's null-Process
    /// comment — for which no window can be located here).</summary>
    internal bool TryResumeRunningGame()
    {
        if (_runningGameProcess is null) return false;

        IntPtr handle = IntPtr.Zero;
        try
        {
            _runningGameProcess.Refresh();
            if (!_runningGameProcess.HasExited) handle = _runningGameProcess.MainWindowHandle;
        }
        catch (InvalidOperationException) { /* process object is stale — fall through to the exe-name lookup below */ }

        // Same stub-then-real-process situation HandleGameProcessExitedAsync guards against: the tracked
        // Process can be the short-lived launcher stub (already exited, or never had a window of its own)
        // while the actual game runs under a different PID with the same exe name.
        if (handle == IntPtr.Zero && !string.IsNullOrEmpty(_runningGameExePath))
        {
            string exeName = Path.GetFileNameWithoutExtension(_runningGameExePath);
            foreach (var candidate in Process.GetProcessesByName(exeName))
            {
                try
                {
                    if (candidate.MainWindowHandle != IntPtr.Zero) { handle = candidate.MainWindowHandle; break; }
                }
                catch (InvalidOperationException) { }
            }
        }

        if (handle == IntPtr.Zero) return false;

        if (IsIconic(handle)) ShowWindow(handle, SwRestore);
        SetForegroundWindow(handle);

        if (_launcherWindow is not null) _launcherWindow.WindowState = WindowState.Minimized;
        return true;
    }

    private void QuitRunningGame()
    {
        try
        {
            _runningGameProcess?.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // already exited, or we don't have permission to kill it — nothing more to do
        }
        CloseOverlay();
    }

    /// <summary>
    /// The actual "launch a game" entry point, called by the launcher window — but the resulting
    /// process tracking/overlay/Discord presence all live here so they survive the window closing.
    /// </summary>
    // Steam/Xbox shell launches (Process.Start returns null for those) give no signal of when the real
    // game actually appears, so "Launching..." can't be cleared precisely — auto-clear after this long
    // instead of leaving it stuck forever if the user tabs back without the window having minimized.
    private static readonly TimeSpan ShellLaunchIndicatorTimeout = TimeSpan.FromSeconds(6);

    public void LaunchGame(MainViewModel vm, GameTileViewModel game)
    {
        if (string.IsNullOrEmpty(game.ExecutablePath)) return;
        if (game.IsLaunching) return; // already launching this one — the whole point of this indicator is to stop repeat clicks here

        game.IsLaunching = true;
        SoundService.PlayConfirm();

        // Runs before Process.Start, not after — this is what actually updates Recently Played's hero
        // card/order, and it should reflect the moment the user chose to launch, not be at the mercy of
        // whether the OS call below happens to succeed. Previously ran after Process.Start with no
        // try/catch around it, so a bad exe path (missing file, permissions) threw before this ever ran,
        // silently leaving Recently Played stale for that launch.
        vm.RecordPlayed(game);

        // WorkingDirectory must be the game's own folder, not the launcher's — otherwise games that
        // resolve assets via a relative path (e.g. ".\data\") fail to find them.
        var startInfo = new ProcessStartInfo(game.ExecutablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath) ?? "",
        };
        Process? process;
        bool launchFailed = false;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            process = null;
            launchFailed = true;
        }

        if (launchFailed)
        {
            // Bad exe path or permissions — nothing was actually launched, so don't minimize, don't set
            // Discord presence, and clear the indicator immediately rather than waiting out the
            // shell-launch timeout. Surfaced via the existing tray balloon (visible even over the
            // fullscreen launcher) since there's no in-window toast mechanism.
            game.IsLaunching = false;
            _trayIcon?.ShowBalloonTip("Couldn't launch " + game.Title, "The game's executable is missing or you don't have permission to run it.", BalloonIcon.Error);
            return;
        }

        // Doesn't need the Process handle, so this runs even for Steam/Xbox shell launches (where
        // Process.Start returns null below) — only downside there is we can't auto-clear it on exit.
        _ = _discord?.SetActivityAsync(game.Title, DateTimeOffset.UtcNow);

        // Minimize rather than destroy on launch — cheaper, no recreate-flicker for the common case.
        // The launcher can still be fully closed (X button) while a game runs; that's handled by
        // OnLauncherClosed same as any other close, independent of this. Unconditional (not just for a
        // trackable Process) — Steam/Xbox launches go through steam://, shell:appsFolder\..., and the
        // launcher should still get out of the way for those exactly the same as a direct exe launch.
        if (_launcherWindow is not null) _launcherWindow.WindowState = WindowState.Minimized;

        // Steam/Xbox launches go through steam://, shell:appsFolder\... — the shell handles those
        // itself and Process.Start returns null, so there's no process to track or overlay for.
        if (process is null)
        {
            _ = ClearLaunchingAfterDelayAsync(game);
            return;
        }

        // A real Process means the OS has genuinely launched it — no need to guess, clear immediately.
        game.IsLaunching = false;

        _runningGameProcess = process;
        _runningGameTitle = game.Title;
        _runningGameExePath = game.ExecutablePath;
        _launcherWindow?.UpdateRunningGame(_runningGameTitle);
        var startedAtUtc = DateTime.UtcNow;

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _ = HandleGameProcessExitedAsync(vm, game, startedAtUtc);
        }
        catch (InvalidOperationException)
        {
            // already exited before we could attach — nothing left to track
        }
    }

    private static readonly TimeSpan RestarterRecheckDelay = TimeSpan.FromSeconds(2);

    // Some apps (a fair few Electron/Squirrel-installed ones — Trello, Discord, Slack, VS Code — plus
    // some games, confirmed live with CS2) launch via a thin stub/updater .exe that spawns the real,
    // longer-lived process and then exits itself within a second or two — so *our* tracked
    // Process.Exited fires almost immediately even though the app the user actually cares about is
    // still very much open. Left unguarded, that made the screen saver ignore a genuinely-still-running
    // app and, worse, brought the launcher window back (OnGameExited -> ShowLauncher) right on top of
    // the game for a moment before the real process took the foreground back — the "minimizes then
    // instantly maximizes" glitch. Heuristic fix: if another process sharing the same exe name is
    // alive, treat this as the stub exiting, not the app. The immediate check alone still raced the
    // replacement process starting, so this rechecks once more after a short delay before giving up.
    private async Task HandleGameProcessExitedAsync(MainViewModel vm, GameTileViewModel game, DateTime startedAtUtc)
    {
        string exeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);
        if (Process.GetProcessesByName(exeName).Length > 0) return;

        await Task.Delay(RestarterRecheckDelay);
        if (Process.GetProcessesByName(exeName).Length > 0) return; // ponytail: fixed recheck delay, not a full process-tree watch — good enough to dodge the restart race

        await Dispatcher.BeginInvoke(() =>
        {
            // Rounded up, with a 1-minute floor — a real session (however short) should always show
            // *something* rather than silently vanishing. The previous floor-and-skip-if-zero logic
            // ((int)TotalMinutes, only recorded if > 0) meant any session under 60 real seconds recorded
            // no playtime at all, which is exactly what a quick manual test looks like.
            int minutes = Math.Max(1, (int)Math.Ceiling((DateTime.UtcNow - startedAtUtc).TotalMinutes));
            vm.RecordPlaytime(game, minutes);
            OnGameExited();
        });
    }

    private static async Task ClearLaunchingAfterDelayAsync(GameTileViewModel game)
    {
        await Task.Delay(ShellLaunchIndicatorTimeout);
        game.IsLaunching = false;
    }

    private void OnGameExited()
    {
        _runningGameProcess = null;
        _runningGameTitle = null;
        _runningGameExePath = null;
        _launcherWindow?.UpdateRunningGame(null);
        CloseOverlay();
        _ = _discord?.SetIdleActivityAsync(); // back to "Browsing the library" rather than clearing to nothing
        ShowLauncher(); // Steam-like: bring the launcher back automatically once the game closes
    }

    /// <summary>Real quit — tears down every core-owned resource, not just the window. Also called by the
    /// power menu's "Shut Down Cartridge OS" option, not just the tray icon.</summary>
    internal void ExitApplication()
    {
        _singleInstancePipeCts?.Cancel();
        _idleTimer?.Stop();
        foreach (var window in _screenSaverWindows.ToArray()) window.Close();
        foreach (var window in _screenSaverBlankWindows.ToArray()) window.Close();
        _gamepad?.Stop();
        _overlayHotkey?.Dispose();
        _discord?.Dispose();
        CloseOverlay();
        _launcherWindow?.Close();
        _trayIcon?.Dispose();
        _coreWindow?.Close();

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        Shutdown();
    }
}
