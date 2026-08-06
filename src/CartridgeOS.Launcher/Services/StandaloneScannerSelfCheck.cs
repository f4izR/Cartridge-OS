using System.IO;
using System.Linq;
using CartridgeOS.Core.Scanning;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-standalone`.
/// Exits 0 on pass, 1 on fail. Builds a real fake "Program Files"-shaped temp directory
/// (a real game folder, a launcher-hub folder that should be blocked, an empty folder)
/// and checks the scanner only surfaces the real game.
/// </summary>
public static class StandaloneScannerSelfCheck
{
    public static bool Run()
    {
        string root = Path.Combine(Path.GetTempPath(), $"cartridgeos-selfcheck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            CreateFakeInstall(root, "My Cool Game", "MyCoolGame.exe", 40_000_000);
            CreateFakeInstall(root, "Steam", "steam.exe", 5_000_000);           // launcher hub, must be blocked
            CreateFakeInstall(root, "Common Files", "somelib.exe", 1_000_000);  // vendor hub, must be blocked
            Directory.CreateDirectory(Path.Combine(root, "Empty Vendor Folder")); // no exe at all, must be skipped

            var results = new StandaloneExecutableScanner().Scan([root]);

            if (results.Count != 1) return false;
            if (results[0].Title != "My Cool Game") return false;
            if (Path.GetFileName(results[0].ExecutablePath) != "MyCoolGame.exe") return false;

            return true;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CreateFakeInstall(string root, string folderName, string exeName, int sizeBytes)
    {
        string dir = Path.Combine(root, folderName);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, exeName), new byte[sizeBytes]);
    }
}
