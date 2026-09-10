namespace CartridgeOS.Core.Models;

public sealed class AppSettings
{
    /// <summary>Whether the Home carousel auto-advances on its own timer (MainViewModel.AdvanceHomeCarousel)
    /// or only moves in response to the user's own input.</summary>
    public bool HomeCarouselAutoCycleEnabled { get; set; } = true;

    /// <summary>Whether the launcher runs as an always-on-top, chrome-less fullscreen "console" (default,
    /// matching a game console dashboard) or a normal bordered window at a fixed size. See MainWindow.xaml's
    /// Window.Style DataTrigger and App.ShowLauncher.</summary>
    public bool FullscreenEnabled { get; set; } = true;

    /// <summary>Whether every game in the library appears in the Home carousel (default) or only the ones
    /// individually opted in via Game.IncludeInHomeCarousel — see MainViewModel.RefreshHomeCarouselSlots.</summary>
    public bool HomeShowAllGames { get; set; } = true;

    /// <summary>The app's accent color pair (hex), used for buttons, selection highlights, and the brand
    /// gradient — see Launcher's ThemeService. Defaults match the original hardcoded Theme.xaml colors.</summary>
    public string ThemeAccentColor1 { get; set; } = "#51E7ED";
    public string ThemeAccentColor2 { get; set; } = "#168DDC";

    /// <summary>Most-recently-used "Find More Games" scan directories, most-recent-first, capped at
    /// MainViewModel.MaxScanDirectories. Empty means "no directory chosen yet — use the default sweep".</summary>
    public List<string> ScanDirectories { get; set; } = [];

    /// <summary>Which fixed drive's storage stats the Recently Played "System Overview" panel shows
    /// (e.g. "D:\"). Null means "not chosen yet — use the system drive".</summary>
    public string? StorageDriveLetter { get; set; }

    public bool NavigationSoundEnabled { get; set; } = true;
    public bool ConfirmSoundEnabled { get; set; } = true;
    public bool TabSwitchSoundEnabled { get; set; } = true;

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

    /// <summary>User-added "quick launch" music apps (Spotify, etc.) — shown on Home as launchable icons
    /// whenever nothing is currently playing, see MainViewModel.MusicApps and HomeView.xaml's music row.</summary>
    public List<MusicAppEntry> MusicApps { get; set; } = [];
}

public sealed class MusicAppEntry
{
    public required string Name { get; set; }
    public required string ExecutablePath { get; set; }
}
