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
    // (they have a big top-level exe that isn't actually a game — e.g. Steam\steam.exe). Scan() skips
    // these entirely; ScanRecursive() also never treats the hub folder itself as a candidate, but does
    // still recurse into it — real games often live a few folders deep inside one (Riot Games\VALORANT,
    // Steam\steamapps\common\<Game>), which is exactly what a "scan whole drive" pass needs to find.
    private static readonly string[] BlockedFolderPrefixes =
    [
        "Steam", "GOG Galaxy", "Battle.net", "Origin", "EA Desktop", "EA Games", "Ubisoft Game Launcher",
        "Epic Games", "Riot Games", "Common Files", "WindowsApps", "Internet Explorer", "Windows",
        "Microsoft", "NVIDIA", "Intel", "Realtek", "Reference Assemblies", "MSBuild", "dotnet",
        "InstallShield Installation Information", "Uninstall Information",
    ];

    // Folders ScanRecursive won't even descend into — real system/junk directories, not launcher hubs
    // that might have a real game nested inside (those are BlockedFolderPrefixes above, still recursed
    // into). Dev-tooling noise (node_modules/.git/.vs/obj) is here too since a "whole drive" scan on a
    // dev machine would otherwise crawl every repo's build output and dependency tree for no benefit.
    private static readonly string[] SkipRecursionFolderNames =
    [
        "$Recycle.Bin", "System Volume Information", "ProgramData", "Recovery", "PerfLogs", "Config.Msi",
        "MSOCache", "Windows", "node_modules", ".git", ".vs", "obj",
    ];

    // Folder names that just wrap the real exe one or more levels deep and shouldn't become the
    // displayed game title — e.g. Riot's "Riot Games\VALORANT\live\VALORANT.exe" should show as
    // "VALORANT", not "live" (the reported bug this list exists to fix). Climbed past when found as
    // the exe's immediate parent (or an ancestor of it) during a recursive scan.
    private static readonly string[] WrapperFolderNames =
        ["live", "bin", "binaries", "win64", "win32", "x64", "x86", "retail", "shipping", "release"];

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

    /// <summary>"Scan whole drive" — unlike Scan() (which only checks one level directly under each root),
    /// this walks the whole subtree under each root looking for an install-shaped folder anywhere in it.
    /// The game's title always comes from the nearest real (non-wrapper, non-hub) ancestor folder name, not
    /// necessarily the one the exe file sits directly in — see WrapperFolderNames' doc comment for why that
    /// distinction matters. Once a folder is claimed as a game, its own subtree isn't searched further (no
    /// point finding a second "game" nested inside one already found). maxDepth bounds how many folders deep
    /// each root is walked, mainly for time — an unbounded walk from a drive root could take a long while.</summary>
    public List<Game> ScanRecursive(IEnumerable<string> roots, int maxDepth = 8)
    {
        var games = new List<Game>();
        foreach (var root in roots)
            if (Directory.Exists(root))
                ScanRecursiveInto(root, candidateName: null, depth: 0, maxDepth, games);
        return games;
    }

    private static void ScanRecursiveInto(string dir, string? candidateName, int depth, int maxDepth, List<Game> games)
    {
        string folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        if (SkipRecursionFolderNames.Any(n => string.Equals(n, folderName, StringComparison.OrdinalIgnoreCase))) return;

        DirectoryInfo info;
        try { info = new DirectoryInfo(dir); } catch (IOException) { return; }
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return; // avoid junction/symlink loops

        bool isHub = BlockedFolderPrefixes.Any(p => folderName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        bool isWrapper = WrapperFolderNames.Contains(folderName, StringComparer.OrdinalIgnoreCase);

        // A hub folder (Steam, Riot Games, ...) never becomes the candidate name itself — its real children
        // (steamapps\common\<Game>, VALORANT, ...) each establish their own name once we descend into them.
        // A wrapper folder (live, bin, Win64, ...) keeps whatever name was already established above it,
        // rather than overwriting it with its own uninformative name.
        string? candidateNameHere = isHub ? candidateName : isWrapper ? candidateName ?? folderName : folderName;

        if (!isHub && candidateNameHere is not null)
        {
            string? exe = ExecutableHeuristics.FindLikelyGameExecutable(dir);
            if (exe is not null)
            {
                games.Add(new Game { Title = candidateNameHere, ExecutablePath = exe });
                return; // claimed — don't also surface something nested inside this game's own folder
            }
        }

        if (depth >= maxDepth) return;

        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(dir).ToList(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; }

        foreach (var child in children)
            ScanRecursiveInto(child, candidateNameHere, depth + 1, maxDepth, games);
    }
}
