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
    // appmanifest_*.acf doesn't expose an app "type" (that only lives in Steam's binary appinfo.vdf
    // cache), so there's no general way to tell a redistributable/tool from a real game locally. 228980
    // ("Steamworks Common Redistributables") is a fixed, well-known exception present in almost every
    // Steam library regardless of what the user actually owns — worth excluding by name since it's not
    // something anyone would ever want to see as a "game" tile.
    private const string SteamworksCommonRedistributablesAppId = "228980";

    public List<Game> Scan()
    {
        string? steamPath = FindSteamInstallPath();
        if (steamPath is null) return [];

        // A library folder can legitimately show up more than once (e.g. libraryfolders.vdf listing the
        // main Steam install path again alongside the paths already seeded into FindLibraryPaths' set),
        // which previously produced duplicate tiles for the same game — dedupe by appid to guarantee one
        // entry per install regardless of how many times its manifest was found.
        var seenAppIds = new HashSet<string>();
        var games = new List<Game>();
        foreach (var libraryPath in FindLibraryPaths(steamPath))
        {
            string steamappsDir = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(steamappsDir)) continue;

            foreach (var manifestFile in Directory.EnumerateFiles(steamappsDir, "appmanifest_*.acf"))
            {
                var (appId, name) = SteamVdf.ExtractAppState(File.ReadAllText(manifestFile));
                if (appId is null || name is null) continue;
                if (appId == SteamworksCommonRedistributablesAppId) continue;
                if (!seenAppIds.Add(appId)) continue;

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
        string cacheDir = Path.Combine(steamPath, "appcache", "librarycache");

        // Steam clients since ~2022 nest images under a per-appid folder; older clients wrote a flat
        // "{appid}_library_600x900.jpg" file directly in librarycache. Check both.
        string nested = Path.Combine(cacheDir, appId, "library_600x900.jpg");
        if (File.Exists(nested)) return nested;

        string flat = Path.Combine(cacheDir, $"{appId}_library_600x900.jpg");
        return File.Exists(flat) ? flat : null;
    }
}
