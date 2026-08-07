using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Input;
using CartridgeOS.Launcher.Input;
using CartridgeOS.Launcher.Services;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher;

public partial class MainWindow : Window
{
    // Must match the tile Width + 2*Margin set in the ItemContainerStyle in MainWindow.xaml.
    private const double TileFootprintWidth = 220 + 2 * 12;

    // Must match BackgroundArt's / CustomBackgroundArt's Opacity in MainWindow.xaml.
    private const double BackgroundArtOpacity = 0.85;
    private const double CustomBackgroundArtOpacity = 0.95;

    private readonly GamepadWatcher _gamepad = new();
    private readonly MouseEmulator _mouse = new();
    private GlobalHotkey? _overlayHotkey;
    private OverlayWindow? _overlayWindow;
    private Process? _runningGameProcess;
    private string? _runningGameTitle;
    private readonly DiscordRichPresence _discord = new();

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;

        // New artwork swaps in instantly (the Image binding just changes Source); fade it back in
        // for a soft crossfade feel instead of a hard cut, PS5-menu style.
        vm.PropertyChanged += (_, e) =>
        {
            (FrameworkElement? target, double opacity) = e.PropertyName switch
            {
                nameof(MainViewModel.SelectedGame) => (BackgroundArt, BackgroundArtOpacity),
                nameof(MainViewModel.CustomWallpaperImage) => (CustomBackgroundArt, CustomBackgroundArtOpacity),
                _ => (null, 0),
            };
            target?.BeginAnimation(OpacityProperty, new DoubleAnimation(0, opacity, TimeSpan.FromMilliseconds(250)));
        };

        Loaded += (_, _) =>
        {
            _gamepad.ButtonPressed += OnGamepadButton;
            _gamepad.RightStickMoved += _mouse.Move;
            _gamepad.RightTriggerChanged += _mouse.SetLeftButtonDown;
            _gamepad.Start();

            // Windows' foreground-lock can leave a debugger-launched window without keyboard focus. Force it.
            Activate();
            GameGrid.Focus();

            // Needs a real window handle, hence wiring it here rather than the constructor.
            _overlayHotkey = new GlobalHotkey(this);
            _overlayHotkey.Pressed += ToggleOverlay;

            _ = _discord.ConnectAsync(); // no-op if Discord isn't running; safe to fire-and-forget
        };
        Closed += (_, _) =>
        {
            _gamepad.Stop();
            _overlayHotkey?.Dispose();
            _discord.Dispose();
            ((MainViewModel)DataContext).StopBackgroundRescanning();
        };

        // Keyboard equivalents of gamepad nav/A/Y — don't rely on native ListBox/VirtualizingWrapPanel arrow-key
        // handling, it only moved selection correctly for Up/Down, not Left/Right.
        PreviewKeyDown += (_, e) =>
        {
            var vm = (MainViewModel)DataContext;
            GamepadButton? button = e.Key switch
            {
                Key.Left => GamepadButton.DPadLeft,
                Key.Right => GamepadButton.DPadRight,
                Key.Up => GamepadButton.DPadUp,
                Key.Down => GamepadButton.DPadDown,
                Key.Enter or Key.Space => GamepadButton.A,
                Key.Insert => GamepadButton.Y,
                _ => null,
            };
            if (!button.HasValue) return;
            HandleInput(vm, button.Value);
            e.Handled = true; // prevent native ListBox arrow-key handling from also acting on this keypress
        };
    }

    private void OnGamepadButton(GamepadButton button)
    {
        Dispatcher.BeginInvoke(() => HandleInput((MainViewModel)DataContext, button));
    }

    private void HandleInput(MainViewModel vm, GamepadButton button)
    {
        if (vm.Games.Count > 0)
        {
            int columns = Math.Max(1, (int)(GameGrid.ActualWidth / TileFootprintWidth));
            int index = vm.SelectedGame is null ? 0 : vm.Games.IndexOf(vm.SelectedGame);

            int previousIndex = index;
            index = button switch
            {
                GamepadButton.DPadLeft => Math.Max(0, index - 1),
                GamepadButton.DPadRight => Math.Min(vm.Games.Count - 1, index + 1),
                GamepadButton.DPadUp => Math.Max(0, index - columns),
                GamepadButton.DPadDown => Math.Min(vm.Games.Count - 1, index + columns),
                _ => index,
            };

            if (index != previousIndex) SoundService.PlayNavigate();

            vm.SelectedGame = vm.Games[index];
            GameGrid.ScrollIntoView(vm.SelectedGame);
        }

        if (button == GamepadButton.A) LaunchSelected(vm, vm.SelectedGame);
        if (button == GamepadButton.Y && vm.AddGameCommand.CanExecute(null)) vm.AddGameCommand.Execute(null);
        if (button == GamepadButton.Start) ToggleOverlay(); // Xbox "Menu"/PS "Options" — same button that pauses in-game, so it's the natural fit
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RecentGame_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        LaunchSelected(vm, vm.SelectedGame);
    }

    private void LaunchSelected(MainViewModel vm, GameTileViewModel? game)
    {
        // ponytail: no launch-failure UI yet (missing exe, permissions) — add when game launching is its own task.
        if (string.IsNullOrEmpty(game?.ExecutablePath)) return;
        SoundService.PlayConfirm();

        var process = Process.Start(new ProcessStartInfo(game.ExecutablePath) { UseShellExecute = true });
        vm.RecordPlayed(game);

        // Doesn't need the Process handle, so this runs even for Steam/Xbox shell launches (where
        // Process.Start returns null below) — only downside there is we can't auto-clear it on exit.
        _ = _discord.SetActivityAsync(game.Title, DateTimeOffset.UtcNow);

        // Steam/Xbox launches go through steam://, shell:appsFolder\... — the shell handles those
        // itself and Process.Start returns null, so there's no process to track or overlay for.
        if (process is null) return;

        _runningGameProcess = process;
        _runningGameTitle = game.Title;
        WindowState = WindowState.Minimized; // this window is Topmost — stay minimized or it'd sit on top of the game

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
        WindowState = WindowState.Maximized;
        Activate();
        _ = _discord.ClearActivityAsync();
    }

    // Ctrl+Shift+O, fires even without focus (see GlobalHotkey) — only meaningful while a game we
    // launched (and can therefore track) is running; ignored otherwise.
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
        WindowState = WindowState.Maximized;
        Activate();
    }

    private void QuitRunningGame()
    {
        // OnGameExited (wired via process.Exited) handles clearing state and restoring the window
        // once the kill actually takes effect — this just closes the overlay immediately for feedback.
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
}
