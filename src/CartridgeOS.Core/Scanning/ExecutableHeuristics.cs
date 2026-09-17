namespace CartridgeOS.Core.Scanning;

public static class ExecutableHeuristics
{
    private static readonly string[] IgnoredNamePrefixes =
        ["unins", "setup", "install", "crashpad", "vc_redist", "redist", "dxsetup", "helper", "crashreporter"];

    // Folder names that just wrap the real install one or more levels deep and shouldn't become the
    // displayed game title — e.g. Riot's "VALORANT/live" should show as "VALORANT", not "live", and
    // League's "League of Legends/Game" should show as "League of Legends", not "Game". Shared between
    // RiotManifest (walks a JSON-reported install path) and StandaloneExecutableScanner (walks the real
    // filesystem tree) since both hit the same "real title is one level above this wrapper" shape.
    private static readonly string[] WrapperFolderNames =
        ["live", "game", "pbe", "beta", "bin", "binaries", "build", "win64", "win32", "x64", "x86", "retail", "shipping", "release"];

    public static bool IsWrapperFolderName(string folderName) =>
        WrapperFolderNames.Contains(folderName, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when two folder names are "the same name" once punctuation/spacing is ignored — e.g.
    /// "Rocket League" vs "rocketleague". Installers that re-nest the product name as their own subfolder
    /// (common for Unreal Engine games: "Rocket League\rocketleague\Binaries\Win64\...") would otherwise
    /// overwrite the correctly-cased parent folder name with this uninformative repeat.</summary>
    public static bool IsNameRepeat(string folderName, string ancestorName) =>
        string.Equals(StripPunctuation(folderName), StripPunctuation(ancestorName), StringComparison.OrdinalIgnoreCase);

    private static string StripPunctuation(string name) =>
        new([.. name.Where(char.IsLetterOrDigit)]);

    /// <summary>Resolves a display title from an install path by walking from the leaf folder upward,
    /// skipping wrapper folders (<see cref="IsWrapperFolderName"/>) and folders that just repeat their
    /// parent's name (<see cref="IsNameRepeat"/>), and returning the first folder name that's neither.
    /// Falls back to the leaf folder name if every segment turns out to be a wrapper/repeat.</summary>
    public static string ResolveTitleFromPath(string path)
    {
        var segments = path.Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (IsWrapperFolderName(segments[i])) continue;
            if (i > 0 && IsNameRepeat(segments[i], segments[i - 1])) continue;
            return segments[i];
        }

        return segments.Length > 0 ? segments[^1] : path;
    }

    /// <summary>
    /// Guesses the main game executable in an install folder when nothing tells us directly
    /// (the uninstall registry only gives an install location, not the exe to launch).
    /// </summary>
    /// <remarks>
    /// ponytail: "biggest non-installer exe in the top-level folder" is crude but a commonly
    /// effective heuristic for the main game binary. Revisit if real installs show it picking wrong.
    /// </remarks>
    public static string? FindLikelyGameExecutable(string installDir) =>
        FindLikelyGameExecutables(installDir).FirstOrDefault();

    /// <summary>Every plausible game exe directly inside a folder, biggest first — not just the single best
    /// guess FindLikelyGameExecutable returns. A per-game install folder only ever has one real answer, but
    /// a folder the user points the scanner at directly isn't guaranteed to have that shape — it can just as
    /// easily be a flat folder of several unrelated loose exes with no per-game subfolder structure at all
    /// (confirmed live: a test folder with 7 loose exes only ever surfaced 1 game under the single-guess
    /// version). See StandaloneExecutableScanner for how single- vs multi-result folders are titled
    /// differently.</summary>
    public static List<string> FindLikelyGameExecutables(string installDir)
    {
        if (!Directory.Exists(installDir)) return [];

        // Same UnauthorizedAccessException/IOException tolerance as the single-result version used to have
        // inline — a real filesystem walk (especially StandaloneExecutableScanner.ScanRecursive, which
        // calls this on every folder it visits) hits plenty of folders the current user can't read.
        try
        {
            return Directory.EnumerateFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(f => !IgnoredNamePrefixes.Any(p => Path.GetFileName(f).StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => new FileInfo(f).Length)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
