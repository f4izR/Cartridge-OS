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

    public void SetTitle(string title)
    {
        _game.Title = title;
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Portrait boxart path — consumed directly (not via the decoded Artwork bitmap) by MainViewModel
    /// when it needs its own higher-resolution decode, e.g. for the Home background.</summary>
    public string? ArtworkPath => _game.ArtworkPath;

    /// <summary>Wide banner for the Home background, fetched lazily — see MainViewModel.RefreshHomeBackgroundAsync.</summary>
    public string? HeroImagePath => _game.HeroImagePath;

    /// <summary>User-picked override for this game's own Home background — takes priority over HeroImagePath
    /// when set. See MainViewModel.RefreshHomeBackgroundAsync.</summary>
    public string? CustomBackgroundPath => _game.CustomBackgroundPath;

    /// <summary>Only reliably knowable from the exe path itself for Steam (steam:// URI) and Xbox/Store (shell:appsFolder) —
    /// every other launcher (Epic/GOG/Ubisoft/EA/Battle.net/Riot/manual) launches via a plain exe path with no marker to
    /// tell them apart, so this is null there rather than guessing.</summary>
    public string? SourceLabel =>
        ExecutablePath.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ? "STEAM" :
        ExecutablePath.StartsWith("shell:appsFolder", StringComparison.OrdinalIgnoreCase) ? "XBOX" :
        null;

    public bool HasSourceLabel => SourceLabel is not null;

    /// <summary>Total playtime tracked so far. Only accumulates for directly-launched exes — Steam/Xbox shell
    /// launches have no trackable Process to time (same limitation as the in-game overlay, see context.md).</summary>
    public int TotalPlaytimeMinutes => _game.TotalPlaytimeMinutes;

    public string PlaytimeLabel
    {
        get
        {
            if (TotalPlaytimeMinutes <= 0) return "Not played yet";
            int hours = TotalPlaytimeMinutes / 60;
            int minutes = TotalPlaytimeMinutes % 60;
            return hours > 0 ? $"{hours}h {minutes}m total" : $"{minutes}m total";
        }
    }

    public string LastPlayedLabel
    {
        get
        {
            if (LastPlayedUtc is not { } lastPlayed) return "Never played";
            var elapsed = DateTime.UtcNow - lastPlayed;
            if (elapsed.TotalMinutes < 1) return "Just now";
            if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m ago";
            if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}h ago";
            return $"{(int)elapsed.TotalDays}d ago";
        }
    }

    [ObservableProperty]
    private ImageSource? _artwork;

    [ObservableProperty]
    private DateTime? _lastPlayedUtc;

    /// <summary>Drives the tile's "Launching..." overlay — set by App.LaunchGame, the single launch entry point.</summary>
    [ObservableProperty]
    private bool _isLaunching;

    public void MarkPlayedNow() => LastPlayedUtc = DateTime.UtcNow;

    /// <summary>Updates this tile's in-memory playtime after App persists the same amount to the DB — the
    /// launcher window (and this instance) can outlive a play session (minimize, not destroy), so the DB
    /// write alone wouldn't be reflected here until the whole viewmodel graph gets rebuilt from scratch.</summary>
    public void AddPlaytime(int minutes)
    {
        _game.TotalPlaytimeMinutes += minutes;
        OnPropertyChanged(nameof(TotalPlaytimeMinutes));
        OnPropertyChanged(nameof(PlaytimeLabel));
    }

    /// <summary>Doesn't touch the tile's own Artwork bitmap — HeroImagePath is only ever consumed by
    /// MainViewModel for the Home background, never rendered on the tile itself.</summary>
    public void SetHeroImagePath(string heroImagePath)
    {
        _game.HeroImagePath = heroImagePath;
        OnPropertyChanged(nameof(HeroImagePath));
    }

    /// <summary>Null clears the override, falling Home's background back to HeroImagePath/ArtworkPath.</summary>
    public void SetCustomBackgroundPath(string? customBackgroundPath)
    {
        _game.CustomBackgroundPath = customBackgroundPath;
        OnPropertyChanged(nameof(CustomBackgroundPath));
    }

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
