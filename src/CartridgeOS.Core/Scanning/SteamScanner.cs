using CartridgeOS.Core.Models;
using Microsoft.Win32;

namespace CartridgeOS.Core.Scanning;

/// <summary>
/// Finds Steam's installed game library by reading Steam's own registry entry and
/// library-folder/app-manifest files — no Steam Web API, no external process.
/// Games are launched via the steam:// protocol, so no exe path needs to be resolved.
/// </summary>
public sealed class SteamScanner
{
    public List<Game> Scan()
    {
        string? steamPath = FindSteamInstallPath();
        if (steamPath is null) return [];

        var games = new List<Game>();
        foreach (var libraryPath in FindLibraryPaths(steamPath))
        {
            string steamappsDir = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(steamappsDir)) continue;

            foreach (var manifestFile in Directory.EnumerateFiles(steamappsDir, "appmanifest_*.acf"))
            {
                var (appId, name) = SteamVdf.ExtractAppState(File.ReadAllText(manifestFile));
                if (appId is null || name is null) continue;

                games.Add(new Game
                {
                    Title = name,
                    ExecutablePath = $"steam://rungameid/{appId}",
                    ArtworkPath = FindArtwork(steamPath, appId)
                });
            }
        }
        return games;
    }

    private static string? FindSteamInstallPath()
    {
        // ponytail: HKCU\...\SteamPath is written by Steam on every install for the current user — covers the
        // normal case. Skip HKLM/WOW6432Node fallback until a real install shows this isn't enough.
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }

    private static IEnumerable<string> FindLibraryPaths(string steamPath)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamPath };

        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
            paths.UnionWith(SteamVdf.ExtractLibraryPaths(File.ReadAllText(vdfPath)));

        return paths;
    }

    private static string? FindArtwork(string steamPath, string appId)
    {
        string path = Path.Combine(steamPath, "appcache", "librarycache", $"{appId}_library_600x900.jpg");
        return File.Exists(path) ? path : null;
    }
}
