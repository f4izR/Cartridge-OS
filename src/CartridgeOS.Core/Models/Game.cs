namespace CartridgeOS.Core.Models;

public sealed class Game
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string ExecutablePath { get; set; }
    public string? ArtworkPath { get; set; }
    public string? LaunchArgs { get; set; }
    public DateTime? LastPlayedUtc { get; set; }
    public int TotalPlaytimeMinutes { get; set; }

    /// <summary>Wide banner image for the Home screen's full-screen backdrop — separate from ArtworkPath
    /// (portrait boxart), fetched lazily since most of the library never actually shows on Home.</summary>
    public string? HeroImagePath { get; set; }

    /// <summary>User-picked override for this game's own Home background — takes priority over HeroImagePath
    /// when set. Per-game (not a single app-wide wallpaper) so each game can have its own backdrop.</summary>
    public string? CustomBackgroundPath { get; set; }
}
