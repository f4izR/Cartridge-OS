using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Scanning;

/// <summary>
/// Finds Epic Games Launcher's installed library by reading its manifest files —
/// no Epic Web API, no launcher process required to be running.
/// </summary>
public sealed class EpicScanner
{
    public List<Game> Scan()
    {
        string manifestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestDir)) return [];

        var games = new List<Game>();
        foreach (var manifestFile in Directory.EnumerateFiles(manifestDir, "*.item"))
        {
            var game = EpicManifest.Parse(File.ReadAllText(manifestFile));
            if (game is not null) games.Add(game);
        }
        return games;
    }
}
