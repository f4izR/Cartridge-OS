namespace CartridgeOS.Core.Models;

public enum WallpaperMode
{
    SelectedGameArtwork,
    CustomImage,
}

public sealed class AppSettings
{
    public WallpaperMode WallpaperMode { get; set; } = WallpaperMode.SelectedGameArtwork;
    public string? CustomWallpaperPath { get; set; }

    /// <summary>Most-recently-used "Find More Games" scan directories, most-recent-first, capped at
    /// MainViewModel.MaxScanDirectories. Empty means "no directory chosen yet — use the default sweep".</summary>
    public List<string> ScanDirectories { get; set; } = [];

    /// <summary>Which fixed drive's storage stats the Recently Played "System Overview" panel shows
    /// (e.g. "D:\"). Null means "not chosen yet — use the system drive".</summary>
    public string? StorageDriveLetter { get; set; }

    public bool NavigationSoundEnabled { get; set; } = true;
    public bool ConfirmSoundEnabled { get; set; } = true;

    public bool ScreenSaverEnabled { get; set; } = true;
    public int ScreenSaverInactivityMinutes { get; set; } = 1;
    public double ScreenSaverVolume { get; set; } = 0.3;

    /// <summary>Folder overrides for the screen saver's slideshow/music — null means "use the bundled
    /// Assets/ScreenSaver/{Images,Sound} files". Setting one replaces the bundled set entirely rather
    /// than merging with it (simpler mental model: empty = defaults, set = only these).</summary>
    public string? ScreenSaverImagesFolder { get; set; }
    public string? ScreenSaverMusicFolder { get; set; }

    /// <summary>Optional user-supplied API keys for artwork lookup (SteamGridDB/TheGamesDB, both offer
    /// free self-serve keys) — null/empty means "use the bundled key". Lets a user who's worried about
    /// the shared bundled key being rate-limited/abused switch to their own, see ArtworkFetcher.</summary>
    public string? SteamGridDbApiKeyOverride { get; set; }
    public string? TheGamesDbApiKeyOverride { get; set; }
}
