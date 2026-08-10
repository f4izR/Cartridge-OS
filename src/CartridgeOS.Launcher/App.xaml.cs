using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using CartridgeOS.Core.Ipc;
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
    private IGamepadInputTarget? _modalGamepadTarget;
    private GlobalHotkey? _overlayHotkey;
    private DiscordRichPresence? _discord;

    private MainWindow? _launcherWindow;
    private int? _lastSelectedGameId;

    private OverlayWindow? _overlayWindow;
    private Process? _runningGameProcess;
    private string? _runningGameTitle;

    protected override void OnStartup(StartupEventArgs e)
    {
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

        ShowLauncher();
    }

    private PipeResponse HandleSingleInstanceSignal(PipeRequest request)
    {
        if (request.Command != "ShowLauncher") return new PipeResponse(false, $"unknown command '{request.Command}'");
        Dispatcher.BeginInvoke(ShowLauncher);
        return new PipeResponse(true);
    }

    private void OnGamepadAction(GamepadAction action)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // A modal dialog (e.g. ArtworkCropWindow) takes over entirely while it's open — its input must
            // never also reach the launcher window underneath (double-handling a Confirm/Back press).
            if (_modalGamepadTarget is { } target) { target.HandleAction(action); return; }

            // Menu means two different things depending on context: toggle the in-game overlay while a game is
            // running (works with no launcher window open, unlike the old per-window binding), or — when nothing's
            // running — open the selected tile's context menu (Change Wallpaper / Delete Game) in the launcher.
            if (action == GamepadAction.Menu && _runningGameProcess is not null) ToggleOverlay();
            else _launcherWindow?.HandleGamepadAction(action);
        });
    }

    /// <summary>Called by a modal window (e.g. ArtworkCropWindow) on open/close to take over — or release — exclusive gamepad routing.</summary>
    public void SetModalGamepadTarget(IGamepadInputTarget? target) => _modalGamepadTarget = target;

    private void OnRightStickMoved(float x, float y)
    {
        if (_modalGamepadTarget is { } target) Dispatcher.BeginInvoke(() => target.HandleRightStick(x, y));
        else _mouse!.Move(x, y); // pure Win32 P/Invoke, not a WPF object — safe to call straight from the poll thread, no Dispatcher needed
    }

    private void OnRightTriggerChanged(bool held)
    {
        if (_modalGamepadTarget is not null) return; // avoid a stray emulated click landing on the dialog underneath while it's open
        _mouse!.SetLeftButtonDown(held);
    }

    /// <summary>Keeps the overlay's on-screen button prompt matching whatever controller is actually plugged in.</summary>
    private void OnControllerChanged(ControllerKind? kind)
    {
        // Fired from GamepadWatcher's background poll thread — every touch of a bound viewmodel property must
        // marshal to the UI thread, same reason OnGamepadAction does.
        Dispatcher.BeginInvoke(() =>
        {
            _currentController = kind;
            if (_overlayWindow?.DataContext is OverlayViewModel vm) vm.MenuButtonLabel = ControllerGlyphs.Label(kind ?? ControllerKind.Generic, GamepadAction.Menu);
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
            _launcherWindow.Closed += (_, _) => OnLauncherClosed();
            this.MainWindow = _launcherWindow; // Application.MainWindow — qualified to disambiguate from the MainWindow type
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

        var overlayVm = new OverlayViewModel(_runningGameTitle ?? "Game", ReturnToLauncher, QuitRunningGame, _currentController);
        _overlayWindow = new OverlayWindow(overlayVm);
        _overlayWindow.Closed += (_, _) => _overlayWindow = null;
        _overlayWindow.Show();
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
        // ponytail: no launch-failure UI yet (missing exe, permissions) — add when game launching is its own task.
        if (string.IsNullOrEmpty(game.ExecutablePath)) return;
        if (game.IsLaunching) return; // already launching this one — the whole point of this indicator is to stop repeat clicks here

        game.IsLaunching = true;
        SoundService.PlayConfirm();

        // WorkingDirectory must be the game's own folder, not the launcher's — otherwise games that
        // resolve assets via a relative path (e.g. ".\data\") fail to find them.
        var startInfo = new ProcessStartInfo(game.ExecutablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath) ?? "",
        };
        var process = Process.Start(startInfo);
        vm.RecordPlayed(game);

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
        var startedAtUtc = DateTime.UtcNow;

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                // Whole minutes, not fractional — matches the "Xh Ym" display granularity, and avoids
                // recording a few seconds of playtime for a game that failed to start and exited immediately.
                int minutes = (int)(DateTime.UtcNow - startedAtUtc).TotalMinutes;
                if (minutes > 0) vm.RecordPlaytime(game, minutes);
                OnGameExited();
            });
        }
        catch (InvalidOperationException)
        {
            // already exited before we could attach — nothing left to track
        }
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
        CloseOverlay();
        _ = _discord?.ClearActivityAsync();
        ShowLauncher(); // Steam-like: bring the launcher back automatically once the game closes
    }

    /// <summary>Real quit — tears down every core-owned resource, not just the window.</summary>
    private void ExitApplication()
    {
        _singleInstancePipeCts?.Cancel();
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
