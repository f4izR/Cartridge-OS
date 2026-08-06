using System.Text.RegularExpressions;

namespace CartridgeOS.Core.Scanning;

/// <summary>
/// Minimal reader for Valve's KeyValue (VDF) text format — just enough to pull the
/// fields Cartridge OS needs out of libraryfolders.vdf and appmanifest_*.acf files.
/// Not a general VDF parser (no nested-object model, no arrays).
/// </summary>
public static partial class SteamVdf
{
    [GeneratedRegex("""
        "path"\s*"([^"]*)"
        """)]
    private static partial Regex LibraryPathPattern();

    [GeneratedRegex("""
        "appid"\s*"([^"]*)"
        """)]
    private static partial Regex AppIdPattern();

    [GeneratedRegex("""
        "name"\s*"([^"]*)"
        """)]
    private static partial Regex NamePattern();

    public static IEnumerable<string> ExtractLibraryPaths(string libraryFoldersVdfContent)
    {
        foreach (Match match in LibraryPathPattern().Matches(libraryFoldersVdfContent))
            yield return Unescape(match.Groups[1].Value);
    }

    public static (string? AppId, string? Name) ExtractAppState(string appManifestContent)
    {
        var appId = AppIdPattern().Match(appManifestContent);
        var name = NamePattern().Match(appManifestContent);
        return (
            appId.Success ? appId.Groups[1].Value : null,
            name.Success ? Unescape(name.Groups[1].Value) : null
        );
    }

    private static string Unescape(string value) => value.Replace("\\\\", "\\");
}
