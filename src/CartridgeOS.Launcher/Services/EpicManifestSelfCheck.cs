using System.IO;
using CartridgeOS.Core.Scanning;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-epic`.
/// Exits 0 on pass, 1 on fail. Verifies manifest parsing against realistic sample
/// JSON — doesn't require a real Epic Games Launcher install.
/// </summary>
public static class EpicManifestSelfCheck
{
    private const string SampleManifest = """
        {
            "DisplayName": "Fortnite",
            "InstallLocation": "C:\\Program Files\\Epic Games\\Fortnite",
            "LaunchExecutable": "FortniteGame\\Binaries\\Win64\\FortniteClient-Win64-Shipping.exe",
            "AppName": "Fortnite",
            "CatalogNamespace": "fn"
        }
        """;

    public static bool Run()
    {
        var game = EpicManifest.Parse(SampleManifest);
        if (game is null) return false;
        if (game.Title != "Fortnite") return false;
        if (game.ExecutablePath != Path.Combine(
                @"C:\Program Files\Epic Games\Fortnite",
                @"FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe"))
            return false;

        if (EpicManifest.Parse("not json at all") is not null) return false;
        if (EpicManifest.Parse("""{"DisplayName": "Missing fields"}""") is not null) return false;

        return true;
    }
}
