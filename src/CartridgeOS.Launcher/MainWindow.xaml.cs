using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    // Must match HomeCarouselTileStyle's Border Width + 2*Margin in MainWindow.xaml.
    private const double HomeTileFootprintWidth = 240 + 2 * 14;

    // Home's background is crisp/unblurred now (no dimming) — full opacity crossfade.
    private const double BackgroundArtOpacity = 1.0;
    private const double CustomBackgroundArtOpacity = 1.0;

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
                // HomeBackgroundImage, not SelectedGame — the redecoded/hero background loads asynchronously
                // a beat after SelectedGame itself changes, so fading on SelectedGame would fire too early
                // (against whatever the still-old Source was) and miss the swap this animation is meant for.
                nameof(MainViewModel.HomeBackgroundImage) => (BackgroundArt, BackgroundArtOpacity),
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

    /// <summary>Called both by local keyboard handling above and by App forwarding real gamepad actions (App owns the GamepadWatcher).</summary>
    public void HandleGamepadAction(GamepadAction action)
    {
        var vm = (MainViewModel)DataContext;
        var visibleGames = vm.GamesView.Cast<GameTileViewModel>().ToList(); // nav moves through whatever the search filter is currently showing, not the full library
        // This directional math (column count, ScrollIntoView) is specific to the Library grid — running it
        // while Home/Recently Played is the active screen moved selection through a grid the user can't even
        // see, which is exactly the bug this guard fixes. Confirm/Secondary/Menu/tab-cycling stay unguarded,
        // since none of those depend on which grid is on screen.
        if (vm.SelectedScreen == AppScreen.Library && visibleGames.Count > 0)
        {
            int columns = Math.Max(1, (int)(GameGrid.ActualWidth / TileFootprintWidth));
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
            GameGrid.ScrollIntoView(vm.SelectedGame);
        }
        else if (vm.SelectedScreen == AppScreen.RecentlyPlayed)
        {
            HandleRecentlyPlayedNavigation(vm, action);
        }
        else if (vm.SelectedScreen == AppScreen.Home && visibleGames.Count > 0 &&
                 action is GamepadAction.NavigateLeft or GamepadAction.NavigateRight)
        {
            // Single horizontal row — only Left/Right apply, same visibleGames list the carousel itself binds
            // to. Wraps around at either end (mod, not clamp) for the "infinite" PS5-carousel feel — past the
            // last tile brings you back to the first, and vice versa. Mouse users can still scroll the bar
            // normally; this is specifically the keyboard/gamepad interaction.
            int index = vm.SelectedGame is null ? 0 : visibleGames.IndexOf(vm.SelectedGame);
            if (index < 0) index = 0;

            int step = action == GamepadAction.NavigateLeft ? -1 : 1;
            int newIndex = (index + step + visibleGames.Count) % visibleGames.Count;

            if (newIndex != index) SoundService.PlayNavigate();
            vm.SelectedGame = visibleGames[newIndex]; // centering itself happens in HomeCarousel_SelectionChanged, fired by this assignment
        }

        if (action == GamepadAction.Confirm) LaunchSelected(vm, vm.SelectedGame);
        if (action == GamepadAction.Secondary && vm.AddGameCommand.CanExecute(null)) vm.AddGameCommand.Execute(null);
        if (action == GamepadAction.Menu) OpenGameContextMenu(vm);
        if (action == GamepadAction.PreviousTab) CycleScreen(vm, -1);
        if (action == GamepadAction.NextTab) CycleScreen(vm, 1);
    }

    /// <summary>Keyboard/gamepad nav for the Recently Played screen: a fixed 3-row x 2-col layout where row 0
    /// (the hero) spans both columns, and rows 1-2 hold the 2x2 "other recent games" grid beneath it.</summary>
    private static void HandleRecentlyPlayedNavigation(MainViewModel vm, GamepadAction action)
    {
        if (action is not (GamepadAction.NavigateUp or GamepadAction.NavigateDown or GamepadAction.NavigateLeft or GamepadAction.NavigateRight))
            return;

        var others = vm.OtherRecentGames.ToList();
        var grid = new GameTileViewModel?[3, 2];
        grid[0, 0] = vm.ContinuePlayingGame; grid[0, 1] = vm.ContinuePlayingGame; // hero spans both columns
        grid[1, 0] = others.ElementAtOrDefault(0); grid[1, 1] = others.ElementAtOrDefault(1);
        grid[2, 0] = others.ElementAtOrDefault(2); grid[2, 1] = others.ElementAtOrDefault(3);

        var (row, col) = FindPosition(grid, vm.SelectedGame) ?? (0, 0);
        var (newRow, newCol) = action switch
        {
            GamepadAction.NavigateUp => (row - 1, col),
            GamepadAction.NavigateDown => (row + 1, col),
            GamepadAction.NavigateLeft => (row, col - 1),
            GamepadAction.NavigateRight => (row, col + 1),
            _ => (row, col),
        };

        if (newRow is < 0 or > 2 || newCol is < 0 or > 1) return; // off the edge — stay put
        var target = grid[newRow, newCol];
        if (target is null) return; // empty cell (fewer than 4 "other" games right now) — stay put

        if (!ReferenceEquals(target, vm.SelectedGame)) SoundService.PlayNavigate();
        vm.SelectedGame = target;
    }

    private static (int Row, int Col)? FindPosition(GameTileViewModel?[,] grid, GameTileViewModel? item)
    {
        if (item is null) return null;
        for (int r = 0; r < grid.GetLength(0); r++)
            for (int c = 0; c < grid.GetLength(1); c++)
                if (ReferenceEquals(grid[r, c], item)) return (r, c);
        return null;
    }

    /// <summary>L1/R1 (LB/RB) cycle the Home/Recently Played/Library nav bar, wrapping at either end.</summary>
    private static void CycleScreen(MainViewModel vm, int direction)
    {
        var screens = Enum.GetValues<AppScreen>();
        int index = Array.IndexOf(screens, vm.SelectedScreen);
        vm.SelectedScreen = screens[(index + direction + screens.Length) % screens.Length];
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

    /// <summary>Keeps the selected carousel tile centered in the viewport (not just scrolled into view at
    /// whichever edge it happens to enter from) — covers every way selection can change here: keyboard/
    /// gamepad Left-Right, mouse click, and double-click-to-launch (which selects first).</summary>
    private void HomeCarousel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((MainViewModel)DataContext).SelectedGame is not { } game) return;

        int index = HomeCarousel.Items.IndexOf(game);
        if (index < 0) return;
        if (FindDescendant<ScrollViewer>(HomeCarousel) is not { } scrollViewer) return;

        double itemCenter = index * HomeTileFootprintWidth + HomeTileFootprintWidth / 2;
        double targetOffset = itemCenter - scrollViewer.ViewportWidth / 2;
        scrollViewer.ScrollToHorizontalOffset(Math.Max(0, targetOffset));
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }

    private void ContinuePlaying_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        LaunchSelected(vm, vm.ContinuePlayingGame);
    }

    private void HomePlay_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        LaunchSelected(vm, vm.SelectedGame);
    }

    /// <summary>Clicking anywhere on the hero card selects it (lighting up its gradient background via
    /// IsContinuePlayingGameSelected) without launching — the Play button/double-click still do that.</summary>
    private void ContinuePlayingCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.SelectedGame = vm.ContinuePlayingGame;
    }

    private static void LaunchSelected(MainViewModel vm, GameTileViewModel? game)
    {
        if (game is null) return;
        ((App)Application.Current).LaunchGame(vm, game);
    }

    private void GameGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }
}
