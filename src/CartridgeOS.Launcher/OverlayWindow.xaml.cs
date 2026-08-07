using System.Windows;
using CartridgeOS.Launcher.ViewModels;

namespace CartridgeOS.Launcher;

public partial class OverlayWindow : Window
{
    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 24;
            Top = workArea.Bottom - ActualHeight - 24;
        };
    }
}
