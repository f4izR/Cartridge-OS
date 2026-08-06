using CartridgeOS.Core.Scanning;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-steam`.
/// Exits 0 on pass, 1 on fail. Verifies the VDF parsing against realistic sample
/// text — doesn't require a real Steam install (there isn't one in dev/CI).
/// </summary>
public static class SteamScannerSelfCheck
{
    private const string SampleLibraryFoldersVdf = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"contentid"		"123456"
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        		"label"		""
        		"contentid"		"654321"
        	}
        }
        """;

    private const string SampleAppManifest = """
        "AppState"
        {
        	"appid"		"400"
        	"Universe"		"1"
        	"name"		"Portal"
        	"StateFlags"		"4"
        	"installdir"		"Portal"
        }
        """;

    public static bool Run()
    {
        var libraryPaths = SteamVdf.ExtractLibraryPaths(SampleLibraryFoldersVdf).ToList();
        if (libraryPaths.Count != 2) return false;
        if (!libraryPaths.Contains(@"C:\Program Files (x86)\Steam")) return false;
        if (!libraryPaths.Contains(@"D:\SteamLibrary")) return false;

        var (appId, name) = SteamVdf.ExtractAppState(SampleAppManifest);
        if (appId != "400") return false;
        if (name != "Portal") return false;

        var (missingAppId, missingName) = SteamVdf.ExtractAppState("not vdf at all");
        if (missingAppId is not null || missingName is not null) return false;

        return true;
    }
}
