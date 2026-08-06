using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Scanning;

public sealed class RiotScanner
{
    public List<Game> Scan()
    {
        string manifestPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Riot Games", "RiotClientInstalls.json");

        return File.Exists(manifestPath) ? RiotManifest.Parse(File.ReadAllText(manifestPath)) : [];
    }
}
