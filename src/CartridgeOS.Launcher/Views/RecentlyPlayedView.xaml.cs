using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher.Views;

public partial class RecentlyPlayedView : UserControl
{
    public RecentlyPlayedView()
    {
        InitializeComponent();
    }

    /// <summary>Selects this row explicitly (see RecentRowStyle's comment for why — the ListBox's own
    /// SelectedItem stopped being trusted for this). RecentGame_DoubleClick still launches on the second click.</summary>
    private void RecentTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is GameTileViewModel tile)
            ((MainViewModel)DataContext).SelectedGame = tile;
    }

    private void RecentGame_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        MainWindow.LaunchSelected(vm, vm.SelectedGame);
    }

    private void ContinuePlaying_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        MainWindow.LaunchSelected(vm, vm.ContinuePlayingGame);
    }

    /// <summary>Clicking anywhere on the hero card selects it (lighting up its gradient background via
    /// IsContinuePlayingGameSelected) without launching — the Play button/double-click still do that.</summary>
    private void ContinuePlayingCard_Click(object sender, MouseButtonEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.SelectedGame = vm.ContinuePlayingGame;
    }
}
