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
}
