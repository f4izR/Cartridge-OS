using System.ComponentModel;
using System.Diagnostics;
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
        var mouse = new MouseEmulator();
        _gamepad.ButtonPressed += OnGamepadButton;
        _gamepad.RightStickMoved += mouse.Move;
        _gamepad.RightTriggerChanged += mouse.SetLeftButtonDown;
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

    private void OnGamepadButton(GamepadButton button)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (button == GamepadButton.Start) ToggleOverlay(); // Xbox "Menu"/PS "Options" — works with no launcher window open, unlike the old per-window binding
            else _launcherWindow?.HandleGamepadButton(button);
        });
    }

    private void OpenLauncher_Click(object sender, RoutedEventArgs e) => ShowLauncher();

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    /// <summary>Recreates the launcher window if it was closed (restoring the last selection), or just refocuses it if it's already open.</summary>
    private void ShowLauncher()
    {
        if (_launcherWindow is null)
        {
            _launcherWindow = new MainWindow(_lastSelectedGameId);
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

        var overlayVm = new OverlayViewModel(_runningGameTitle ?? "Game", ReturnToLauncher, QuitRunningGame);
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
    public void LaunchGame(MainViewModel vm, GameTileViewModel game)
    {
        // ponytail: no launch-failure UI yet (missing exe, permissions) — add when game launching is its own task.
        if (string.IsNullOrEmpty(game.ExecutablePath)) return;
        SoundService.PlayConfirm();

        var process = Process.Start(new ProcessStartInfo(game.ExecutablePath) { UseShellExecute = true });
        vm.RecordPlayed(game);

        // Doesn't need the Process handle, so this runs even for Steam/Xbox shell launches (where
        // Process.Start returns null below) — only downside there is we can't auto-clear it on exit.
        _ = _discord?.SetActivityAsync(game.Title, DateTimeOffset.UtcNow);

        // Steam/Xbox launches go through steam://, shell:appsFolder\... — the shell handles those
        // itself and Process.Start returns null, so there's no process to track or overlay for.
        if (process is null) return;

        _runningGameProcess = process;
        _runningGameTitle = game.Title;
        // Minimize rather than destroy on launch — cheaper, no recreate-flicker for the common case.
        // The launcher can still be fully closed (X button) while a game runs; that's handled by
        // OnLauncherClosed same as any other close, independent of this.
        if (_launcherWindow is not null) _launcherWindow.WindowState = WindowState.Minimized;

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.BeginInvoke(OnGameExited);
        }
        catch (InvalidOperationException)
        {
            // already exited before we could attach — nothing left to track
        }
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
