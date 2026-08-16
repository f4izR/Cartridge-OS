using System.Text.Json;
using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Scanning;

/// <summary>
/// Parses Riot's own RiotClientInstalls.json — lists every installed Riot game as an
/// "associated_client" entry keyed by install path, plus the Riot Client's own exe path.
/// Pulled out separately so it's testable without a real Riot install.
/// </summary>
public static class RiotManifest
{
    public static List<Game> Parse(string json)
    {
        var games = new List<Game>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("rc_default", out var clientExeEl)) return games;
            string? clientExe = clientExeEl.GetString();
            if (string.IsNullOrEmpty(clientExe)) return games;

            if (!root.TryGetProperty("associated_client", out var associatedEl)) return games;

            foreach (var entry in associatedEl.EnumerateObject())
            {
                // entry.Name is the install path, e.g. "E:/Riot Games/VALORANT/live/" or
                // "C:/Riot Games/League of Legends/Game/" — the leaf folder ("live"/"Game") is just the
                // client/runtime subfolder, not the game's name, so climb past it (see
                // ExecutableHeuristics.ResolveTitleFromPath) instead of taking it at face value.
                string title = ExecutableHeuristics.ResolveTitleFromPath(entry.Name);
                if (string.IsNullOrEmpty(title)) continue;

                // Games launch through the Riot Client itself, not a per-game exe — it handles auth/patching first.
                games.Add(new Game { Title = title, ExecutablePath = clientExe.Replace('/', Path.DirectorySeparatorChar) });
            }
        }
        catch (JsonException) { }
        return games;
    }
}
