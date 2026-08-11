using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    /// <summary>Single click selects the tile (recomputing HomeCarouselSlots around it); a double-click
    /// also launches — same combined pattern the old ListBox's SelectedItem+MouseDoubleClick gave for free,
    /// reimplemented by hand now that the carousel isn't a Selector control anymore.</summary>
    private void HomeCarouselTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not HomeCarouselSlot slot) return;

        var vm = (MainViewModel)DataContext;
        vm.SelectedGame = slot.Game;
        if (e.ClickCount == 2) MainWindow.LaunchSelected(vm, slot.Game);
    }

    private void HomePlay_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        MainWindow.LaunchSelected(vm, vm.SelectedGame);
    }
}
