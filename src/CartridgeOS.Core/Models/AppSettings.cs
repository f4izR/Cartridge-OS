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
}
