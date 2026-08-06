using System.Windows;

namespace CartridgeOS.Launcher;

public partial class ScanResultsWindow : Window
{
    public ScanResultsWindow()
    {
        InitializeComponent();
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
