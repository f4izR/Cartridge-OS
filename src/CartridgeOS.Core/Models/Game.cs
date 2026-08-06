namespace CartridgeOS.Core.Models;

public sealed class Game
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string ExecutablePath { get; set; }
    public string? ArtworkPath { get; set; }
    public string? LaunchArgs { get; set; }
    public DateTime? LastPlayedUtc { get; set; }
}
