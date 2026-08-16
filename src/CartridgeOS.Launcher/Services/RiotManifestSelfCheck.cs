using System.Linq;
using CartridgeOS.Core.Scanning;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-riot`.
/// Exits 0 on pass, 1 on fail. Verifies RiotClientInstalls.json parsing against sample
/// content matching the documented format — no real Riot install needed.
/// </summary>
public static class RiotManifestSelfCheck
{
    // Matches Riot's real on-disk format: the key is the per-game install path (ending in the client/
    // runtime subfolder — "Game" for LoL, "live" for VALORANT), the value is always the shared client exe.
    private const string SampleManifest = """
        {
            "rc_default": "C:/Riot Games/Riot Client/RiotClientServices.exe",
            "rc_live": "C:/Riot Games/Riot Client/RiotClientServices.exe",
            "associated_client": {
                "C:/Riot Games/League of Legends/Game/": "C:/Riot Games/Riot Client/RiotClientServices.exe",
                "C:/Riot Games/VALORANT/live/": "C:/Riot Games/Riot Client/RiotClientServices.exe"
            }
        }
        """;

    public static bool Run()
    {
        var games = RiotManifest.Parse(SampleManifest);
        if (games.Count != 2) return false;
        if (!games.Any(g => g.Title == "League of Legends")) return false;
        if (!games.Any(g => g.Title == "VALORANT")) return false;
        if (games.Any(g => g.ExecutablePath != @"C:\Riot Games\Riot Client\RiotClientServices.exe")) return false;

        if (RiotManifest.Parse("not json").Count != 0) return false;
        if (RiotManifest.Parse("{}").Count != 0) return false;

        return true;
    }
}
