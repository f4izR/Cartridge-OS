using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Scanning;

/// <summary>
/// Finds installed games from a launcher that doesn't publish its own manifest format
/// (GOG, Ubisoft Connect, EA App, Battle.net) by filtering the Windows uninstall registry
/// down to entries whose Publisher matches, then guessing the exe inside each install dir.
/// Less precise than a real manifest reader (Steam/Epic/Riot) but it's the only signal
/// these four launchers reliably share.
/// </summary>
public sealed class PublisherGameScanner(params string[] publisherKeywords)
{
    public List<Game> Scan()
    {
        var games = new List<Game>();
        foreach (var program in new InstalledProgramsScanner().Scan())
        {
            bool isMatch = publisherKeywords.Any(keyword =>
                program.Publisher.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (!isMatch) continue;

            string? exe = ExecutableHeuristics.FindLikelyGameExecutable(program.InstallLocation);
            if (exe is null) continue;

            games.Add(new Game { Title = program.DisplayName, ExecutablePath = exe });
        }
        return games;
    }
}
