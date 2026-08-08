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
    private string? _previousArtworkPath;

    /// <summary>Whether TryRevertArtwork has something to restore — drives the "Revert to Previous Artwork" menu item's enabled state.</summary>
    public bool HasPreviousArtwork { get; private set; }

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

    /// <summary>Drives the tile's "Launching..." overlay — set by App.LaunchGame, the single launch entry point.</summary>
    [ObservableProperty]
    private bool _isLaunching;

    public void MarkPlayedNow() => LastPlayedUtc = DateTime.UtcNow;

    public void SetArtworkPath(string artworkPath)
    {
        _previousArtworkPath = _game.ArtworkPath;
        HasPreviousArtwork = true;
        _game.ArtworkPath = artworkPath;
        _ = LoadArtworkAsync();
    }

    /// <summary>
    /// Single-level undo — restores whatever artwork this game had immediately before its most recent
    /// change, then forgets it (not a full history stack; a second call with nothing new set does nothing).
    /// Returns true and outputs the restored path (possibly null, if the game had no artwork before) on success.
    /// </summary>
    public bool TryRevertArtwork(out string? restoredPath)
    {
        if (!HasPreviousArtwork)
        {
            restoredPath = null;
            return false;
        }

        restoredPath = _previousArtworkPath;
        _game.ArtworkPath = restoredPath;
        HasPreviousArtwork = false;
        _previousArtworkPath = null;

        if (string.IsNullOrEmpty(restoredPath)) Artwork = null; // nothing to load — clear the tile explicitly, LoadArtworkAsync leaves Artwork untouched on a null path
        else _ = LoadArtworkAsync();

        return true;
    }

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
