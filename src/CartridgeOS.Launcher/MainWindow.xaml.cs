using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using CartridgeOS.Launcher.Input;
using CartridgeOS.Launcher.Services;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher;

public partial class MainWindow : Window
{
    // Must match the tile Width + 2*Margin set in the ItemContainerStyle in MainWindow.xaml.
    private const double TileFootprintWidth = 220 + 2 * 12;

    private readonly GamepadWatcher _gamepad = new();
    private readonly MouseEmulator _mouse = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        Loaded += (_, _) =>
        {
            _gamepad.ButtonPressed += OnGamepadButton;
            _gamepad.RightStickMoved += _mouse.Move;
            _gamepad.RightTriggerChanged += _mouse.SetLeftButtonDown;
            _gamepad.Start();

            // Windows' foreground-lock can leave a debugger-launched window without keyboard focus. Force it.
            Activate();
            GameGrid.Focus();
        };
        Closed += (_, _) =>
        {
            _gamepad.Stop();
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
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RecentGame_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        LaunchSelected(vm, vm.SelectedGame);
    }

    private static void LaunchSelected(MainViewModel vm, GameTileViewModel? game)
    {
        // ponytail: no launch-failure UI yet (missing exe, permissions) — add when game launching is its own task.
        if (string.IsNullOrEmpty(game?.ExecutablePath)) return;
        SoundService.PlayConfirm();
        Process.Start(new ProcessStartInfo(game.ExecutablePath) { UseShellExecute = true });
        vm.RecordPlayed(game);
    }
}
