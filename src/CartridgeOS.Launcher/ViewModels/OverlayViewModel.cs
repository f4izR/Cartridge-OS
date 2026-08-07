using System.Windows.Input;
using CartridgeOS.Core;
using CommunityToolkit.Mvvm.Input;

namespace CartridgeOS.Launcher.ViewModels;

public sealed class OverlayViewModel : ViewModelBase
{
    public string GameTitle { get; }
    public ICommand ReturnCommand { get; }
    public ICommand QuitGameCommand { get; }

    public OverlayViewModel(string gameTitle, Action onReturn, Action onQuitGame)
    {
        GameTitle = gameTitle;
        ReturnCommand = new RelayCommand(onReturn);
        QuitGameCommand = new RelayCommand(onQuitGame);
    }
}
