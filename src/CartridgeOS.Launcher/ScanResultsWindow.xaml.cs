using System.Windows;
using System.Windows.Input;

namespace CartridgeOS.Launcher;

public partial class ScanResultsWindow : Window
{
    public ScanResultsWindow()
    {
        InitializeComponent();
    }

    // WindowStyle="None" means there's no native title bar to drag by — the custom title row's
    // background needs to forward left-button-down into DragMove() to stay movable.
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
