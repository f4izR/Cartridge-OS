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

            // Regression check: the user picks a directory via Browse... that IS the game's own folder
            // (exe sitting directly inside it), not a Program-Files-style container of per-game
            // subfolders — reported live (an exe placed directly in a picked folder found nothing).
            string directHitRoot = Path.Combine(Path.GetTempPath(), $"cartridgeos-selfcheck-{Guid.NewGuid():N}", "Bogus EXE");
            Directory.CreateDirectory(directHitRoot);
            try
            {
                File.WriteAllBytes(Path.Combine(directHitRoot, "BogusGame.exe"), new byte[20_000_000]);
                var directHitResults = new StandaloneExecutableScanner().Scan([directHitRoot]);
                if (directHitResults.Count != 1) return false;
                if (directHitResults[0].Title != "Bogus EXE") return false;
                if (Path.GetFileName(directHitResults[0].ExecutablePath) != "BogusGame.exe") return false;
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(directHitRoot)!, recursive: true);
            }

            // Regression check: a folder of several loose exes with no per-game subfolder structure at all
            // (e.g. a handful of games' exes dumped straight into one folder) — reported live, only 1 of 3
            // ever showed up before this, since the single-best-guess heuristic discarded the other 2.
            string multiExeRoot = Path.Combine(Path.GetTempPath(), $"cartridgeos-selfcheck-{Guid.NewGuid():N}", "Loose Exes");
            Directory.CreateDirectory(multiExeRoot);
            try
            {
                File.WriteAllBytes(Path.Combine(multiExeRoot, "First_Game.exe"), new byte[30_000_000]);
                File.WriteAllBytes(Path.Combine(multiExeRoot, "Second Game.exe"), new byte[20_000_000]);
                File.WriteAllBytes(Path.Combine(multiExeRoot, "unins000.exe"), new byte[1_000_000]); // ignored prefix, must still be filtered out
                var multiExeResults = new StandaloneExecutableScanner().Scan([multiExeRoot]);

                if (multiExeResults.Count != 2) return false;
                if (!multiExeResults.Any(g => g.Title == "First Game" && Path.GetFileName(g.ExecutablePath) == "First_Game.exe")) return false;
                if (!multiExeResults.Any(g => g.Title == "Second Game" && Path.GetFileName(g.ExecutablePath) == "Second Game.exe")) return false;
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(multiExeRoot)!, recursive: true);
            }

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
