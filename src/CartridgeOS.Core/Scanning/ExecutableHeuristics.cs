namespace CartridgeOS.Core.Scanning;

public static class ExecutableHeuristics
{
    private static readonly string[] IgnoredNamePrefixes =
        ["unins", "setup", "install", "crashpad", "vc_redist", "redist", "dxsetup", "helper", "crashreporter"];

    /// <summary>
    /// Guesses the main game executable in an install folder when nothing tells us directly
    /// (the uninstall registry only gives an install location, not the exe to launch).
    /// </summary>
    /// <remarks>
    /// ponytail: "biggest non-installer exe in the top-level folder" is crude but a commonly
    /// effective heuristic for the main game binary. Revisit if real installs show it picking wrong.
    /// </remarks>
    public static string? FindLikelyGameExecutable(string installDir)
    {
        if (!Directory.Exists(installDir)) return null;

        var candidates = Directory.EnumerateFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(f => !IgnoredNamePrefixes.Any(p => Path.GetFileName(f).StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return candidates.Count == 0
            ? null
            : candidates.OrderByDescending(f => new FileInfo(f).Length).First();
    }
}
