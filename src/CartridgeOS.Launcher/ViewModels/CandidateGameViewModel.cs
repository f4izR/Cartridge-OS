using CartridgeOS.Core;
using CartridgeOS.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CartridgeOS.Launcher.ViewModels;

public sealed partial class CandidateGameViewModel(Game game) : ViewModelBase
{
    public Game Game => game;
    public string Title => game.Title;
    public string ExecutablePath => game.ExecutablePath;

    [ObservableProperty]
    private bool _isSelected; // default unchecked — user opts in to what's actually a game
}
