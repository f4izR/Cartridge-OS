using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher.Views;

public partial class LibraryView : UserControl
{
    /// <summary>Read by MainWindow.HandleGamepadAction (column-math nav, ScrollIntoView) and OpenGameContextMenu
    /// (ItemContainerGenerator lookup) — gamepad routing lives in the shell, not per-screen.</summary>
    public ListBox GameGrid => GameGridListBox;

    public LibraryView()
    {
        InitializeComponent();
    }

    /// <summary>Double-click to launch, matching Home's carousel and Recently Played's tiles — this was
    /// previously missing entirely on Library, so a mouse user double-clicking a tile here did nothing
    /// (no launch means App.LaunchGame/RecordPlayed never ran, so Recently Played never picked it up).</summary>
    private void GameGridListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        MainWindow.LaunchSelected(vm, vm.SelectedGame);
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
    /// both trigger paths (right-click sets PlacementTarget natively, MainWindow.OpenGameContextMenu sets it explicitly).</summary>
    private void GameTileContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var tile = (menu.PlacementTarget as FrameworkElement)?.DataContext as GameTileViewModel;
        // Tag-based lookup, not FindName: a MenuItem declared inside a resource ContextMenu (as opposed to a
        // ControlTemplate) isn't registered in any NameScope FindName can resolve at runtime — that's why this
        // was silently a no-op before. menu.Items enumeration works regardless of naming.
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
}
