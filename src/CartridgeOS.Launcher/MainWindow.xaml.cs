using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            GetActiveGameGrid().Focus();
        };
        Closed += (_, _) =>
        {
            vm.StopBackgroundRescanning();
            vm.StopStatusUpdates();
        };

        // Keyboard equivalents of gamepad nav/A/Y — don't rely on native ListBox/VirtualizingWrapPanel arrow-key
        // handling, it only moved selection correctly for Up/Down, not Left/Right.
        PreviewKeyDown += (_, e) =>
        {
            GamepadAction? action = e.Key switch
            {
                Key.Left => GamepadAction.NavigateLeft,
                Key.Right => GamepadAction.NavigateRight,
                Key.Up => GamepadAction.NavigateUp,
                Key.Down => GamepadAction.NavigateDown,
                Key.Enter or Key.Space => GamepadAction.Confirm,
                Key.Insert => GamepadAction.Secondary,
                Key.Apps => GamepadAction.Menu, // the Windows "context menu" key — keyboard equivalent of gamepad Menu/Start/Options
                _ => null,
            };
            if (!action.HasValue) return;
            HandleGamepadAction(action.Value);
            e.Handled = true; // prevent native ListBox arrow-key handling from also acting on this keypress
        };
    }

    /// <summary>Get the active game grid based on the current tab selection.</summary>
    private ListBox GetActiveGameGrid()
    {
        var vm = (MainViewModel)DataContext;
        return vm.CurrentTab == 0 ? GamesTabGameGrid : GameGrid;
    }

    /// <summary>Called both by local keyboard handling above and by App forwarding real gamepad actions (App owns the GamepadWatcher).</summary>
    public void HandleGamepadAction(GamepadAction action)
    {
        var vm = (MainViewModel)DataContext;
        var activeGrid = GetActiveGameGrid();
        var visibleGames = vm.GamesView.Cast<GameTileViewModel>().ToList(); // nav moves through whatever the search filter is currently showing, not the full library
        if (visibleGames.Count > 0)
        {
            int columns = Math.Max(1, (int)(activeGrid.ActualWidth / TileFootprintWidth));
            int index = vm.SelectedGame is null ? 0 : visibleGames.IndexOf(vm.SelectedGame);
            if (index < 0) index = 0; // selected game got filtered out from under us

            int previousIndex = index;
            index = action switch
            {
                GamepadAction.NavigateLeft => Math.Max(0, index - 1),
                GamepadAction.NavigateRight => Math.Min(visibleGames.Count - 1, index + 1),
                GamepadAction.NavigateUp => Math.Max(0, index - columns),
                GamepadAction.NavigateDown => Math.Min(visibleGames.Count - 1, index + columns),
                _ => index,
            };

            if (index != previousIndex) SoundService.PlayNavigate();

            vm.SelectedGame = visibleGames[index];
            activeGrid.ScrollIntoView(vm.SelectedGame);
        }

        if (action == GamepadAction.Confirm) LaunchSelected(vm, vm.SelectedGame);
        if (action == GamepadAction.Secondary && vm.AddGameCommand.CanExecute(null)) vm.AddGameCommand.Execute(null);
        if (action == GamepadAction.Menu) OpenGameContextMenu(vm);
    }

    /// <summary>Opens the selected tile's context menu (Change Wallpaper / Delete Game) — the gamepad Menu/Start/Options
    /// equivalent of right-clicking a tile. Only reachable when no game is running (App intercepts Menu for the
    /// in-game overlay toggle in that case instead — see App.OnGamepadAction).</summary>
    private void OpenGameContextMenu(MainViewModel vm)
    {
        if (vm.SelectedGame is null) return;
        if (GameGrid.ItemContainerGenerator.ContainerFromItem(vm.SelectedGame) is not ListBoxItem { ContextMenu: { } menu } container) return;

        menu.PlacementTarget = container;
        menu.IsOpen = true;
    }

    /// <summary>Right-click doesn't move ListBox selection on its own — select the tile under the cursor first so the
    /// context menu it's about to open (Change Wallpaper / Delete Game) always acts on the game actually clicked.</summary>
    private void GameTile_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is GameTileViewModel tile)
            ((MainViewModel)DataContext).SelectedGame = tile;
    }

    private void ChangeWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (vm.ChangeArtworkCommand.CanExecute(null)) vm.ChangeArtworkCommand.Execute(null);
    }

    /// <summary>Enables/disables "Revert to Previous Artwork" for whatever game the menu is about to show for — covers
    /// both trigger paths (right-click sets PlacementTarget natively, OpenGameContextMenu sets it explicitly).</summary>
    private void GameTileContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var tile = (menu.PlacementTarget as FrameworkElement)?.DataContext as GameTileViewModel;
        // Tag-based lookup, not FindName: a MenuItem declared inside a Window.Resources object graph (as
        // opposed to a ControlTemplate) isn't registered in any NameScope FindName can resolve at runtime —
        // that's why this was silently a no-op before. menu.Items enumeration works regardless of naming.
        var revertItem = menu.Items.OfType<MenuItem>().FirstOrDefault(mi => Equals(mi.Tag, "RevertArtwork"));
        if (revertItem is not null) revertItem.IsEnabled = tile?.HasPreviousArtwork ?? false;
    }

    private void RevertArtwork_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (vm.RevertArtworkCommand.CanExecute(null)) vm.RevertArtworkCommand.Execute(null);
    }

    private void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (vm.RemoveGameCommand.CanExecute(null)) vm.RemoveGameCommand.Execute(null);
    }

    /// <summary>Forwarded by App from GamepadWatcher.ControllerBatteryChanged, and pushed once with the current value right after this window is created.</summary>
    public void UpdateControllerBattery(int? percent) => ((MainViewModel)DataContext).ControllerBatteryPercent = percent;

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
