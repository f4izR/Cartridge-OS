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
    // Launcher hub folders that would otherwise look like a "game" to the heuristic (they have a big
    // top-level exe that isn't actually a game — e.g. GOG Galaxy.exe). Scan() skips these entirely;
    // ScanRecursive() also never treats the hub folder itself as a candidate, but does still recurse
    // into it — real games can live a few folders deep inside one, which a "scan whole drive" pass needs
    // to find for launchers only covered by the registry-based PublisherGameScanner (which can miss
    // install-location variants). Steam/Epic/Riot are deliberately NOT here even though the same is
    // structurally true of them: SteamScanner/EpicScanner/RiotScanner already read those launchers' own
    // manifests directly and exhaustively, so there is no real game this heuristic walk would find that
    // isn't already covered — only risk of surfacing the launcher's own internal helper binaries (Steam's
    // "bin\cef\cef.win64\steamwebhelper.exe", "bin\hardwareupdater\hardwareupdater.exe", ...) as fake
    // "games". They're in SkipFolderNames below instead — never descended into at all.
    private static readonly string[] LauncherHubFolderNames =
    [
        "GOG Galaxy", "Battle.net", "Origin", "EA Games", "Ubisoft Game Launcher",
    ];

    // Folders that will never contain a real game — OS/vendor components, dev tooling, and standalone
    // utilities that happen to sit as a single top-level Program Files folder with one exe (exactly the
    // shape the heuristic is looking for). Unlike LauncherHubFolderNames, ScanRecursive never descends
    // into these at all: nothing genuine lives inside "NVIDIA Corporation" or "Microsoft Office", so
    // walking their subtree only produces noise (this is why a first pass over a real dev machine
    // surfaced 90+ false positives — every NVIDIA/Office/Visual-Studio-Build-Tools sub-component got
    // treated as its own "game" candidate once inside the hub). Growing this list is expected; it can
    // never be exhaustive, but each real false positive found should be added here rather than papered
    // over per-instance.
    private static readonly string[] SkipFolderNames =
    [
        "Common Files", "WindowsApps", "Internet Explorer", "Windows", "Windows NT", "Windows Defender",
        "Windows Defender Advanced Threat Protection", "Windows Kits", "Windows Mail", "Windows Media Player",
        "Windows Multimedia Platform", "Windows Photo Viewer", "Windows Portable Devices", "Windows Security",
        "Microsoft", "Microsoft Office", "Microsoft Office 15", "Microsoft Visual Studio", "Microsoft.NET",
        "Microsoft SDKs", "Microsoft Update Health Tools", "Microsoft OneDrive", "ModifiableWindowsApps",
        "NVIDIA Corporation", "Intel", "Realtek", "Reference Assemblies", "MSBuild", "dotnet",
        "InstallShield Installation Information", "Uninstall Information", "Google", "Application Verifier",
        "WindowsPowerShell", "Crashpad", "PCHealthCheck", "EasyAntiCheat_EOS", "EasyAntiCheat", "BattlEye",
        "Riot Vanguard", "RUXIM", "Attack Shark Software", "Git", "nodejs", "WinRAR", "WizTree", "qBittorrent",
        // EA/Origin's launcher installs itself (and its anti-cheat helper, "EA\AC") to Program Files —
        // distinct from EA Games above, which is where legacy Origin-style *game* installs actually land.
        // Nothing under the launcher's own "Electronic Arts\EA Desktop\..." or "EA\AC\..." is ever a game.
        "Electronic Arts", "EA Desktop", "EA", "DroidCam",
        // Covered by their own dedicated manifest-based scanners (see LauncherHubFolderNames' comment) —
        // never descended into, so their internal helper binaries can't leak through as fake "games".
        "Steam", "Epic Games", "Riot Games",
        "$Recycle.Bin", "System Volume Information", "ProgramData", "Recovery", "PerfLogs", "Config.Msi",
        "MSOCache", "node_modules", ".git", ".vs", "obj",
    ];

    /// <summary>Default sweep: recurses (bounded, see ScanRecursive) into just Program Files/Games — the
    /// same narrow set of roots DefaultScanRoots always used — rather than only checking their immediate
    /// children. A single-level check missed real games installed a folder or two deep (e.g. Unreal
    /// Engine's "Rocket League\rocketleague\Binaries\Win64\RocketLeague.exe") while still surfacing every
    /// unrelated top-level exe (WinRAR, Git, qBittorrent, anti-cheat drivers, ...) as a "possible game" —
    /// recursing here is safe because SkipFolderNames now prunes the large non-game subtrees (NVIDIA,
    /// Office, Windows Kits, ...) that made an earlier "just always recurse" attempt too noisy.</summary>
    public List<Game> Scan() => ScanRecursive(DefaultScanRoots());

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
                if (IsHubOrSkip(folderName)) continue;

                string? exe = ExecutableHeuristics.FindLikelyGameExecutable(folder);
                if (exe is null) continue;

                games.Add(new Game { Title = folderName, ExecutablePath = exe });
            }
        }
        return games;
    }

    /// <summary>"Scan whole drive" — unlike the single-level Scan(roots) overload, this walks the whole
    /// subtree under each root looking for an install-shaped folder anywhere in it. The game's title
    /// always comes from the nearest real (non-wrapper, non-hub, non-repeat) ancestor folder name, not
    /// necessarily the one the exe file sits directly in — see ExecutableHeuristics' wrapper-name helpers
    /// for why that distinction matters. Once a folder is claimed as a game, its own subtree isn't
    /// searched further (no point finding a second "game" nested inside one already found). maxDepth
    /// bounds how many folders deep each root is walked, mainly for time — an unbounded walk from a drive
    /// root could take a long while, though SkipFolderNames pruning whole non-game subtrees up front does
    /// most of the actual work of keeping this fast.</summary>
    public List<Game> ScanRecursive(IEnumerable<string> roots, int maxDepth = 8)
    {
        var games = new List<Game>();
        foreach (var root in roots)
            if (Directory.Exists(root))
                ScanRecursiveInto(root, candidateName: null, depth: 0, maxDepth, games);
        return games;
    }

    private static bool IsHub(string folderName) =>
        LauncherHubFolderNames.Any(n => string.Equals(n, folderName, StringComparison.OrdinalIgnoreCase));

    private static bool IsSkip(string folderName) =>
        SkipFolderNames.Any(n => string.Equals(n, folderName, StringComparison.OrdinalIgnoreCase));

    private static bool IsHubOrSkip(string folderName) => IsHub(folderName) || IsSkip(folderName);

    private static void ScanRecursiveInto(string dir, string? candidateName, int depth, int maxDepth, List<Game> games)
    {
        string folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        if (IsSkip(folderName)) return; // never descend — nothing genuine lives inside these

        DirectoryInfo info;
        try { info = new DirectoryInfo(dir); } catch (IOException) { return; }
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return; // avoid junction/symlink loops

        bool isHub = IsHub(folderName);
        bool isWrapper = ExecutableHeuristics.IsWrapperFolderName(folderName)
            || (candidateName is not null && ExecutableHeuristics.IsNameRepeat(folderName, candidateName));

        // A hub folder (Steam, Riot Games, ...) never becomes the candidate name itself, and — unlike a
        // wrapper folder — it also discards whatever candidate name was established above it: its real
        // children (steamapps\common\<Game>, VALORANT, ...) each establish their own fresh name once we
        // descend into them. Discarding rather than passing the old name through matters for a hub sitting
        // right under the scan root (e.g. Program Files (x86)\Steam\bin\drivers.exe) — passing the parent
        // name through used to make the scan root's own folder name ("Program Files (x86)") leak in as
        // the title of anything found a couple levels inside the hub.
        //
        // A wrapper folder (live, bin, Win64, ...) — or one that just repeats the name already established
        // above it (e.g. "Rocket League\rocketleague") — keeps that established name rather than
        // overwriting it with its own uninformative one, but critically does NOT invent a name from its
        // own folderName when nothing real has been established yet (candidateName is null): a wrapper
        // folder with no real ancestor (Steam\bin, once Steam has reset the name above) has nothing
        // meaningful to offer, so it must not be surfaced at all rather than self-naming "bin".
        string? candidateNameHere = isHub ? null : isWrapper ? candidateName : folderName;

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
