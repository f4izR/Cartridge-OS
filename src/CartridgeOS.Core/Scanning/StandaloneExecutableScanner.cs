using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Scanning;

/// <summary>
/// Finds games that aren't registered with any launcher by scanning common install roots
/// for folders that look like a game (top-level folder containing a plausible main exe).
/// Pure heuristic — no manifest, no registry — so it WILL produce false positives.
/// Callers must let the user confirm before adding anything found this way, unlike the
/// launcher-specific scanners which are trusted to auto-add.
/// </summary>
public sealed class StandaloneExecutableScanner
{
    // Known launcher/vendor hub folders that would otherwise look like a "game" to the heuristic
    // (they have a big top-level exe that isn't actually a game — e.g. Steam\steam.exe).
    private static readonly string[] BlockedFolderPrefixes =
    [
        "Steam", "GOG Galaxy", "Battle.net", "Origin", "EA Desktop", "EA Games", "Ubisoft Game Launcher",
        "Epic Games", "Riot Games", "Common Files", "WindowsApps", "Internet Explorer", "Windows",
        "Microsoft", "NVIDIA", "Intel", "Realtek", "Reference Assemblies", "MSBuild", "dotnet",
        "InstallShield Installation Information", "Uninstall Information",
    ];

    public List<Game> Scan() => Scan(DefaultScanRoots());

    /// <summary>
    /// Program Files on the system drive, plus "Program Files" and "Games" folders on every
    /// other fixed drive — covers the common case of a game library installed on a second disk.
    /// </summary>
    private static IEnumerable<string> DefaultScanRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            if (drive.RootDirectory.FullName.StartsWith(Path.GetPathRoot(Environment.SystemDirectory)!, StringComparison.OrdinalIgnoreCase))
                continue; // system drive already covered above

            yield return Path.Combine(drive.RootDirectory.FullName, "Program Files");
            yield return Path.Combine(drive.RootDirectory.FullName, "Games");
        }
    }

    /// <summary>Overload for testing against a synthetic directory tree instead of real Program Files.</summary>
    public List<Game> Scan(IEnumerable<string> roots)
    {
        var games = new List<Game>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                string folderName = Path.GetFileName(folder);
                if (BlockedFolderPrefixes.Any(p => folderName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string? exe = ExecutableHeuristics.FindLikelyGameExecutable(folder);
                if (exe is null) continue;

                games.Add(new Game { Title = folderName, ExecutablePath = exe });
            }
        }
        return games;
    }
}
