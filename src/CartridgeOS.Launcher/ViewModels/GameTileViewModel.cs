using System.Windows.Media;
using CartridgeOS.Core;
using CartridgeOS.Core.Models;
using CartridgeOS.Launcher.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CartridgeOS.Launcher.ViewModels;

public sealed partial class GameTileViewModel : ViewModelBase
{
    // Matches the artwork slot inside the tile template in MainWindow.xaml (220px tile, 10px margins each side).
    private const int ArtworkDecodeWidth = 200;

    private readonly Game _game;

    public GameTileViewModel(Game game)
    {
        _game = game;
        LastPlayedUtc = game.LastPlayedUtc;
        _ = LoadArtworkAsync();
    }

    public int Id => _game.Id;
    public string Title => _game.Title;
    public string ExecutablePath => _game.ExecutablePath;

    [ObservableProperty]
    private ImageSource? _artwork;

    [ObservableProperty]
    private DateTime? _lastPlayedUtc;

    public void MarkPlayedNow() => LastPlayedUtc = DateTime.UtcNow;

    private async Task LoadArtworkAsync()
    {
        if (string.IsNullOrEmpty(_game.ArtworkPath)) return;
        try
        {
            Artwork = await ArtworkCache.LoadAsync(_game.ArtworkPath, ArtworkDecodeWidth);
        }
        catch (Exception)
        {
            // ponytail: swallow and keep the placeholder tile — surfacing artwork load errors isn't a V1 requirement.
        }
    }
}
