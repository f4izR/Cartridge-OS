using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Input;
using CartridgeOS.Launcher.Input;
using CartridgeOS.Launcher.Services;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher;

/// <summary>
/// Disposable UI: created and destroyed by App on demand, never kept resident just to avoid a
/// recreate. Owns nothing that needs to survive its own closing — gamepad/hotkey listeners,
/// running-game tracking, overlay, and Discord presence all live in App instead. See progress.md.
/// </summary>
public partial class MainWindow : Window
{
    // Must match the tile Width + 2*Margin set in the ItemContainerStyle in MainWindow.xaml.
    private const double TileFootprintWidth = 220 + 2 * 12;

    // Must match BackgroundArt's / CustomBackgroundArt's Opacity in MainWindow.xaml.
    private const double BackgroundArtOpacity = 0.85;
    private const double CustomBackgroundArtOpacity = 0.95;

    /// <summary>Read by App right as this window closes, to remember what to re-select next time it opens.</summary>
    public int? CurrentSelectedGameId => ((MainViewModel)DataContext).SelectedGame?.Id;

    public MainWindow(int? restoreSelectedGameId = null)
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

        if (restoreSelectedGameId is { } id)
        {
            var restored = vm.Games.FirstOrDefault(g => g.Id == id);
            if (restored is not null) vm.SelectedGame = restored;
        }

        Loaded += (_, _) =>
        {
            // Windows' foreground-lock can leave a debugger-launched window without keyboard focus. Force it.
            Activate();
            GameGrid.Focus();
        };
        Closed += (_, _) => vm.StopBackgroundRescanning();

        // Keyboard equivalents of gamepad nav/A/Y — don't rely on native ListBox/VirtualizingWrapPanel arrow-key
        // handling, it only moved selection correctly for Up/Down, not Left/Right.
        PreviewKeyDown += (_, e) =>
        {
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
            HandleGamepadButton(button.Value);
            e.Handled = true; // prevent native ListBox arrow-key handling from also acting on this keypress
        };
    }

    /// <summary>Called both by local keyboard handling above and by App forwarding real gamepad button presses (App owns the GamepadWatcher).</summary>
    public void HandleGamepadButton(GamepadButton button)
    {
        var vm = (MainViewModel)DataContext;
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
        if (game is null) return;
        ((App)Application.Current).LaunchGame(vm, game);
    }
}
