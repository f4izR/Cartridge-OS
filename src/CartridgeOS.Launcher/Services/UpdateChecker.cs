using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Nudge-only update check — no silent download/install (see production-readiness.md for why a full
/// auto-updater was deliberately skipped: needs code signing first, or every update prompts a
/// SmartScreen/UAC warning). Fetches a small JSON file this app's own GitHub repo hosts, compares
/// against the running build's own <see cref="AssemblyInformationalVersionAttribute"/>/Version
/// (csproj's &lt;Version&gt;), and returns non-null only when a newer release exists — the caller
/// just shows a dismissible banner with a link, nothing more.
/// </summary>
public static class UpdateChecker
{
    private const string VersionUrl = "https://raw.githubusercontent.com/f4izR/Cartridge-OS/main/version.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public sealed record UpdateInfo(string Version, string ReleaseUrl);

    /// <summary>Best-effort and silent on any failure (offline, DNS, malformed JSON, repo file missing) —
    /// an update check is a nice-to-have and must never affect startup or look like an error.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            string json = await Http.GetStringAsync(VersionUrl);
            var info = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (info is null) return null;

            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            if (!Version.TryParse(info.Version, out var latest)) return null;

            // Version's own comparison treats an omitted Revision as -1, not 0 — a "1.0.0" build (parses to
            // Revision -1) would otherwise compare as newer than the assembly's own "1.0.0.0" (Revision 0)
            // even at the exact same release. Normalize both to Major.Minor.Build before comparing.
            var currentNormalized = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
            var latestNormalized = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));

            return latestNormalized > currentNormalized ? info : null;
        }
        catch
        {
            return null;
        }
    }
}
