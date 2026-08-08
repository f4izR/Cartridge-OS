using System.Windows.Input;
using CartridgeOS.Core;
using CartridgeOS.Launcher.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CartridgeOS.Launcher.ViewModels;

public sealed partial class OverlayViewModel : ViewModelBase
{
    public string GameTitle { get; }
    public ICommand ReturnCommand { get; }
    public ICommand QuitGameCommand { get; }

    /// <summary>Controller-specific glyph for the toggle-overlay button, e.g. "Start" (Xbox) or "Options" (PlayStation).</summary>
    [ObservableProperty]
    private string _menuButtonLabel;

    public OverlayViewModel(string gameTitle, Action onReturn, Action onQuitGame, ControllerKind? controller)
    {
        GameTitle = gameTitle;
        ReturnCommand = new RelayCommand(onReturn);
        QuitGameCommand = new RelayCommand(onQuitGame);
        _menuButtonLabel = ControllerGlyphs.Label(controller ?? ControllerKind.Generic, GamepadAction.Menu);
    }
}
