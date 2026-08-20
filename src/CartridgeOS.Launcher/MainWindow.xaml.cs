using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;
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
            // Focus the Window itself, not a specific screen's control (this used to always call
            // GameGrid.Focus() — broken as soon as Home became the default screen instead of Library, since
            // the Library grid sits Collapsed there and WPF silently refuses to focus a collapsed element,
            // leaving nothing focused at all. All nav is handled explicitly via PreviewKeyDown below
            // regardless of which control has focus, so the Window itself is a focus target that's always valid.
            Activate();
            Focus();
        };

        // MainViewModel.UiScale is seeded from SystemParameters.WorkArea up front (see its own comment),
        // which covers the normal case with no layout-timing dependency. This just re-syncs it if the
        // window ends up on a differently-sized monitor than whatever was primary at construction time
        // (dragged across monitors, or maximized onto a secondary display) — cheap enough to run on every
        // resize rather than trying to detect only the moves that matter.
        SizeChanged += (_, _) => vm.UpdateUiScale(ActualWidth);
        Closed += (_, _) =>
        {
            vm.StopBackgroundRescanning();
            vm.StopStatusUpdates();
        };

        // Topmost="True" (XAML) keeps this window above every other non-topmost window regardless of
        // which one actually has focus — that's the whole point for the console-dashboard look, but it
        // means alt-tabbing to (or clicking the taskbar icon for) an ordinary app like Chrome left that
        // app's window rendering *behind* this one, with no visible way to actually reach it (reported by
        // user testing). Minimizing on Deactivated fixes that the same way launching a game already does
        // (see App.LaunchGame) — a minimized Topmost window doesn't render, so whatever the user just
        // switched to becomes visible. Guarded to same-process only: this window also "deactivates" when
        // one of its own child dialogs (PowerMenuWindow, ArtworkCropWindow, ScanResultsWindow) opens, and
        // minimizing out from under an owned dialog would be exactly the wrong thing to do there.
        Deactivated += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) return;
            if (IsForegroundWindowInThisProcess()) return;
            WindowState = WindowState.Minimized;
        };

        // Keyboard equivalents of gamepad nav/A/Y — don't rely on native ListBox/VirtualizingWrapPanel arrow-key
        // handling, it only moved selection correctly for Up/Down, not Left/Right.
        PreviewKeyDown += (_, e) =>
        {
            // Don't hijack keys a focused TextBox needs for normal editing (search box, Settings' API key
            // fields) — this handler runs Preview/tunneling, so without this guard Left/Right/Enter/Escape
            // never reach the TextBox at all (e.Handled = true below swallows them first), breaking cursor
            // movement and Escape/Enter entirely for anyone typing. Confirmed live: typing "abcdef" then
            // pressing Left three times then "XYZ" produced "abcdefXYZ" instead of "abcXYZdef".
            if (Keyboard.FocusedElement is TextBox) return;

            GamepadAction? action = e.Key switch
            {
                Key.Left => GamepadAction.NavigateLeft,
                Key.Right => GamepadAction.NavigateRight,
                Key.Up => GamepadAction.NavigateUp,
                Key.Down => GamepadAction.NavigateDown,
                Key.Enter or Key.Space => GamepadAction.Confirm,
                Key.Insert => GamepadAction.Secondary,
                Key.Apps => GamepadAction.Menu, // the Windows "context menu" key — keyboard equivalent of gamepad Menu/Options button
                Key.Escape => GamepadAction.Back,
                Key.Tab => GamepadAction.ToggleSettings,
                Key.F4 => GamepadAction.Power, // keyboard equivalent of the controller Guide/Xbox/PS button
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

        // Every action below acts directly on the ViewModel/background grid rather than going through
        // WPF focus, so without this guard a minimized window or an open Settings/Search panel didn't stop
        // Confirm/Menu/Power etc. from reaching straight through to whatever tile was still selected
        // underneath (e.g. opening a tile's context menu while Settings covered the screen).
        if (WindowState == WindowState.Minimized) return;

        if (_openGameContextMenu is { } contextMenu)
        {
            // Same MoveFocus routing as the Settings/Search block below — the context menu's own MenuItems
            // handle Up/Down highlighting once one of them has focus (seeded in OpenGameContextMenu), this
            // just needs to get out of the way of the Library-grid math further down while it's open.
            Debug.WriteLine($"[MainWindow] ContextMenu guard: {action}, focused={Keyboard.FocusedElement}");
            switch (action)
            {
                case GamepadAction.NavigateUp: MoveFocusFrom(FocusNavigationDirection.Up); break;
                case GamepadAction.NavigateDown: MoveFocusFrom(FocusNavigationDirection.Down); break;
                // These MenuItems wire up via Click="..." handlers in XAML, not Command — raising Click
                // directly invokes them (and closes the menu, MenuItem's default Click behavior) without
                // depending on synthetic keyboard state (see the Settings block's comment below for why
                // that approach — tried first — didn't actually work).
                case GamepadAction.Confirm when Keyboard.FocusedElement is MenuItem item: item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); break;
                case GamepadAction.Back or GamepadAction.Menu: contextMenu.IsOpen = false; break;
            }
            return;
        }

        if (vm.IsSettingsOpen || vm.IsSearchOpen)
        {
            // Settings/Search cover the whole screen — route D-Pad/stick through normal WPF focus
            // traversal instead of the Library-grid math below (same MoveFocus approach PowerMenuWindow
            // uses for its own button list), since there was previously no fallback at all here and
            // D-Pad nav was silently dead while either panel was open.
            Debug.WriteLine($"[MainWindow] Settings/Search guard: {action}, focused={Keyboard.FocusedElement}, IsSettingsOpen={vm.IsSettingsOpen}");

            // Belt-and-suspenders: if focus somehow isn't inside the Settings panel at all when a nav
            // press arrives (e.g. it opened via mouse click on the gear icon, which never moves keyboard
            // focus the way SettingsPanel's own IsVisibleChanged handler does), re-seed it before trying
            // to move from it — otherwise MoveFocus below silently does nothing.
            if (vm.IsSettingsOpen && action is GamepadAction.NavigateUp or GamepadAction.NavigateDown
                or GamepadAction.NavigateLeft or GamepadAction.NavigateRight &&
                (Keyboard.FocusedElement is not DependencyObject focused || !IsDescendantOf(SettingsHost, focused)))
            {
                Debug.WriteLine("[MainWindow] focus wasn't inside Settings — re-seeding via FocusFirst");
                SettingsHost.FocusFirst();
            }

            // A synthetic KeyDown (tried first, see RaiseKeyOnFocused below — left in for other callers)
            // doesn't work here: WPF's KeyEventArgs.KeyStates is derived from the REAL physical keyboard
            // device, not from anything the KeyEventArgs constructor takes, so WPF's own directional-nav
            // and control keyboard handling see a "key" that (as far as the real keyboard is concerned)
            // isn't actually down and ignore it — confirmed by live testing, focus never moved. Calling
            // each control's own API directly instead: reliable regardless of input-device state.
            switch (action)
            {
                case GamepadAction.NavigateUp when Keyboard.FocusedElement is ComboBox combo: if (!AdjustComboBox(combo, -1)) MoveFocusFrom(FocusNavigationDirection.Up); break;
                case GamepadAction.NavigateDown when Keyboard.FocusedElement is ComboBox combo: if (!AdjustComboBox(combo, +1)) MoveFocusFrom(FocusNavigationDirection.Down); break;
                case GamepadAction.NavigateLeft when Keyboard.FocusedElement is Slider slider: if (!AdjustSlider(slider, -1)) MoveFocusFrom(FocusNavigationDirection.Left); break;
                case GamepadAction.NavigateRight when Keyboard.FocusedElement is Slider slider: if (!AdjustSlider(slider, +1)) MoveFocusFrom(FocusNavigationDirection.Right); break;
                case GamepadAction.NavigateUp: MoveFocusFrom(FocusNavigationDirection.Up); break;
                case GamepadAction.NavigateDown: MoveFocusFrom(FocusNavigationDirection.Down); break;
                case GamepadAction.NavigateLeft: MoveFocusFrom(FocusNavigationDirection.Left); break;
                case GamepadAction.NavigateRight: MoveFocusFrom(FocusNavigationDirection.Right); break;
                case GamepadAction.Confirm: ConfirmFocused(); break;
                case GamepadAction.Back when vm.IsSettingsOpen: vm.IsSettingsOpen = false; break;
                case GamepadAction.Back when vm.IsSearchOpen: vm.IsSearchOpen = false; break;
                case GamepadAction.ToggleSettings: vm.ToggleSettingsCommand.Execute(null); break;
                case GamepadAction.ToggleSearch when vm.SelectedScreen == AppScreen.Library: vm.ToggleSearchCommand.Execute(null); break;
            }
            Debug.WriteLine($"[MainWindow] after handling: focused={Keyboard.FocusedElement}");
            return;
        }

        var visibleGames = vm.GamesView.Cast<GameTileViewModel>().ToList(); // nav moves through whatever the search filter is currently showing, not the full library
        // This directional math (column count, ScrollIntoView) is specific to the Library grid — running it
        // while Home/Recently Played is the active screen moved selection through a grid the user can't even
        // see, which is exactly the bug this guard fixes. Confirm/Secondary/Menu/tab-cycling stay unguarded,
        // since none of those depend on which grid is on screen.
        if (vm.SelectedScreen == AppScreen.Library && visibleGames.Count > 0)
        {
            double tileFootprintWidth = vm.TileWidth + 2 * 12; // must match the tile Width + Margin in LibraryView.xaml's GameTileStyle
            int columns = Math.Max(1, (int)(LibraryScreen.GameGrid.ActualWidth / tileFootprintWidth));
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
            LibraryScreen.GameGrid.ScrollIntoView(vm.SelectedGame);
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
            vm.SelectedGame = visibleGames[newIndex];
        }

        if (action == GamepadAction.Confirm) LaunchSelected(vm, vm.SelectedGame);
        if (action == GamepadAction.Secondary && vm.AddGameCommand.CanExecute(null)) vm.AddGameCommand.Execute(null);
        // Library-only: the tile's ContextMenu (Change Wallpaper / Delete Game) is only ever attached to
        // Library's ListBoxItem style (LibraryView.xaml's GameTileStyle) — Home's carousel and Recently
        // Played's tiles are a completely different visual tree with no context menu of their own. Without
        // this guard, pressing Menu on Home/Recently Played still found *some* stale/off-screen Library
        // ListBoxItem container (if one happened to be realized from an earlier visit) and anchored the
        // menu to it — which looked like "opens anywhere on the screen" since that container had nothing
        // to do with the tile actually on screen.
        if (action == GamepadAction.Menu && vm.SelectedScreen == AppScreen.Library) OpenGameContextMenu(vm);
        if (action == GamepadAction.PreviousTab) CycleScreen(vm, -1);
        if (action == GamepadAction.NextTab) CycleScreen(vm, 1);
        // Back closes whatever's on top rather than navigating screens — same "back out of the overlay,
        // don't touch anything underneath" convention every console dashboard uses B/Circle for. Settings
        // takes priority since it can be open regardless of which screen search belongs to.
        if (action == GamepadAction.Back)
        {
            if (vm.IsSettingsOpen) vm.IsSettingsOpen = false;
            else if (vm.IsSearchOpen) vm.IsSearchOpen = false;
        }
        if (action == GamepadAction.ToggleSettings) vm.ToggleSettingsCommand.Execute(null);
        // Search only exists on Library (see the search pill's own Visibility binding) — no-op elsewhere
        // rather than opening a search box the user can't see.
        if (action == GamepadAction.ToggleSearch && vm.SelectedScreen == AppScreen.Library) vm.ToggleSearchCommand.Execute(null);
        if (action == GamepadAction.Power) OpenPowerMenu();
    }

    /// <summary>Moves keyboard focus in <paramref name="direction"/> from whatever's currently focused —
    /// the plain-element fallback for the Settings/Search and context-menu guards above, once Slider/
    /// ComboBox (which need their value adjusted, not their focus moved) have been special-cased out.</summary>
    private static void MoveFocusFrom(FocusNavigationDirection direction)
    {
        bool moved = (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(direction)) ?? false;
        Debug.WriteLine($"[MainWindow] MoveFocus {direction} -> moved={moved}, focused={Keyboard.FocusedElement}");
    }

    /// <summary>D-Pad Left/Right on a focused Slider adjusts its value by one SmallChange step instead of
    /// moving focus off it — Sliders in Settings are laid out horizontally, so Left/Right maps naturally.
    /// Returns false at Minimum/Maximum (nothing left to adjust) so the caller falls back to MoveFocus
    /// instead of trapping the D-Pad on a slider that's already maxed/minned out.</summary>
    private static bool AdjustSlider(Slider slider, int direction)
    {
        double newValue = Math.Clamp(slider.Value + direction * slider.SmallChange, slider.Minimum, slider.Maximum);
        if (newValue == slider.Value) return false;
        Debug.WriteLine($"[MainWindow] Slider {slider.Value} -> {newValue}");
        slider.Value = newValue;
        return true;
    }

    /// <summary>D-Pad Up/Down on a focused ComboBox cycles its selection instead of moving focus off it —
    /// simpler than opening the dropdown Popup and navigating a separate focus scope inside it, and gives
    /// the same end result (a different value picked) with one D-Pad press per step instead of two.
    /// Returns false at the first/last item so the caller falls back to MoveFocus instead of trapping the
    /// D-Pad on a ComboBox that's already at either end of its list.</summary>
    private static bool AdjustComboBox(ComboBox combo, int direction)
    {
        if (combo.Items.Count == 0) return false;
        int newIndex = Math.Clamp(combo.SelectedIndex + direction, 0, combo.Items.Count - 1);
        if (newIndex == combo.SelectedIndex) return false;
        Debug.WriteLine($"[MainWindow] ComboBox {combo.SelectedIndex} -> {newIndex}");
        combo.SelectedIndex = newIndex;
        return true;
    }

    /// <summary>Confirm (A/✕) on whatever's focused in Settings/Search — mirrors what a real click on that
    /// control would do, dispatched by type since there's no single WPF API that means "activate this."</summary>
    private static void ConfirmFocused()
    {
        switch (Keyboard.FocusedElement)
        {
            // RadioButton before ToggleButton: it derives from ToggleButton, and clicking an already-checked
            // RadioButton in real WPF never unchecks it (only checking a different one in the group does) —
            // toggling it via IsChecked = !IsChecked like a plain CheckBox would leave the group with nothing
            // selected.
            case RadioButton radio: radio.IsChecked = true; break;
            case ToggleButton toggle: toggle.IsChecked = !(toggle.IsChecked ?? false); break;
            case ComboBox combo: combo.IsDropDownOpen = !combo.IsDropDownOpen; break;
            case ButtonBase button: button.Command?.Execute(button.CommandParameter); break;
        }
        Debug.WriteLine($"[MainWindow] ConfirmFocused on {Keyboard.FocusedElement}");
    }

    /// <summary>Walks up the visual tree from <paramref name="node"/> looking for <paramref name="ancestor"/> —
    /// used to check whether the currently focused element is actually inside the Settings panel before
    /// trusting MoveFocus to start from it (see the Settings/Search guard above).</summary>
    private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject node)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }
        return false;
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

    /// <summary>Tracked while a gamepad-opened context menu is up, so HandleGamepadAction's top-of-method
    /// guard can route D-Pad/Confirm/Back into it (see there) instead of falling through to the Library
    /// grid navigation underneath.</summary>
    private ContextMenu? _openGameContextMenu;

    /// <summary>Opens the selected tile's context menu (Change Wallpaper / Delete Game) — the gamepad Menu/Options
    /// equivalent of right-clicking a tile (Xbox "Menu"/hamburger button, PS "Options" button). Effectively only
    /// reachable when no game is running — the window is minimized while a game runs (App.LaunchSelected), and
    /// HandleGamepadAction's minimized-window guard above no-ops every action in that state, this one included.</summary>
    private void OpenGameContextMenu(MainViewModel vm)
    {
        if (vm.SelectedGame is null) return;

        // Scrolling into view first, then waiting a layout pass before opening, matters for virtualized
        // items: a container that was just scrolled to (or hasn't been arranged since the last scroll) can
        // still report a stale/zero-size RenderSize the instant this runs, which throws the Popup's Center
        // placement math off — the menu would open somewhere other than the tile even though PlacementTarget
        // is correct.
        LibraryScreen.GameGrid.ScrollIntoView(vm.SelectedGame);
        Dispatcher.BeginInvoke(() =>
        {
            if (LibraryScreen.GameGrid.ItemContainerGenerator.ContainerFromItem(vm.SelectedGame) is not ListBoxItem { ContextMenu: { } menu } container)
            {
                Debug.WriteLine("[MainWindow] OpenGameContextMenu: no container found for selected game");
                return;
            }

            Debug.WriteLine($"[MainWindow] OpenGameContextMenu: anchoring to container at {container.PointToScreen(new Point(0, 0))}, size={container.RenderSize}");

            // ContextMenu.Placement defaults to MousePoint, which opened this wherever the emulated cursor last
            // sat rather than near the tile a gamepad press has no mouse position to anchor to — pin it to the
            // tile itself instead, same as a real console dashboard's per-item options menu.
            menu.Placement = PlacementMode.Center;
            menu.PlacementTarget = container;
            _openGameContextMenu = menu;
            menu.Closed += (_, _) => _openGameContextMenu = null;
            menu.IsOpen = true;
            Dispatcher.BeginInvoke(() => menu.Items.OfType<MenuItem>().FirstOrDefault()?.Focus());
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Forwarded by App from GamepadWatcher.ControllerBatteryChanged, and pushed once with the current value right after this window is created.</summary>
    public void UpdateControllerBattery(int? percent) => ((MainViewModel)DataContext).ControllerBatteryPercent = percent;

    /// <summary>Forwarded by App on LT+RT toggle, and pushed once with the current value right after this window is created — the header pill is the only lock indicator now (see App.OnGamepadAction).</summary>
    public void UpdateCursorLocked(bool locked) => ((MainViewModel)DataContext).IsCursorLocked = locked;

    public void ShowUpdateAvailable(string version, string releaseUrl) => ((MainViewModel)DataContext).ShowUpdateAvailable(version, releaseUrl);

    private PowerMenuWindow? _powerMenuWindow;

    private void Power_Click(object sender, RoutedEventArgs e) => OpenPowerMenu();

    /// <summary>Opens (or, if already open, closes) the power menu — replaces the old bare minimize/close
    /// title-bar buttons with Turn Off System / Restart System / Exit to Desktop / Shut Down Cartridge OS.</summary>
    private void OpenPowerMenu()
    {
        if (_powerMenuWindow is not null)
        {
            _powerMenuWindow.Close();
            return;
        }

        var app = (App)Application.Current;
        var vm = new PowerMenuViewModel(ExitToDesktop, app.ExitApplication, app.CurrentController);
        _powerMenuWindow = new PowerMenuWindow(vm);
        _powerMenuWindow.Closed += (_, _) => _powerMenuWindow = null;
        _powerMenuWindow.Show();
    }

    // What the old bare X button did — closes just this window; App-level services (tray icon, gamepad
    // watcher, Discord presence) keep running until "Shut Down Cartridge OS" is chosen instead.
    private void ExitToDesktop()
    {
        _powerMenuWindow?.Close();
        Close();
    }

    internal static void LaunchSelected(MainViewModel vm, GameTileViewModel? game)
    {
        if (game is null) return;
        ((App)Application.Current).LaunchGame(vm, game);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>True if whatever's currently in the foreground (or nothing — 0 while a window is
    /// mid-transition) belongs to this same process, e.g. one of our own dialogs taking focus.
    /// internal (not private) — App reuses this to gate the stick-driven cursor to our own windows.</summary>
    internal static bool IsForegroundWindowInThisProcess()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return true;
        GetWindowThreadProcessId(foreground, out uint processId);
        return processId == Environment.ProcessId;
    }
}
