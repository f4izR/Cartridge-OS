using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Update check — still no *silent* background install (see production-readiness.md for why a full
/// auto-updater was deliberately skipped: needs code signing first, or every update prompts a
/// SmartScreen/UAC warning regardless). What this does do: the user clicking the update banner now
/// downloads the real installer directly and hands it to Windows to run, instead of just opening the
/// GitHub release page and making them find/download/run it themselves — same trust posture as a
/// manual download (identical SmartScreen prompt either way, since neither is signed yet), just fewer
/// clicks. Fetches a small JSON file this app's own GitHub repo hosts, compares against the running
/// build's own <see cref="AssemblyInformationalVersionAttribute"/>/Version (csproj's &lt;Version&gt;),
/// and returns non-null only when a newer release exists.
/// </summary>
public static class UpdateChecker
{
    private const string VersionUrl = "https://raw.githubusercontent.com/f4izR/Cartridge-OS/main/version.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <param name="DownloadUrl">Direct link to the installer exe for this release. Null/empty on a
    /// malformed or older version.json — callers fall back to ReleaseUrl (open the release page) rather
    /// than failing outright.</param>
    /// <param name="Sha256">Expected SHA-256 of the installer, lowercase hex, no separators. Null/empty
    /// means "not published yet" (e.g. the release script hasn't been updated to compute it) — the
    /// downloader skips verification rather than refusing to install, since an absent hash isn't
    /// evidence of tampering, just of an older/incomplete version.json.</param>
    public sealed record UpdateInfo(string Version, string ReleaseUrl, string? DownloadUrl = null, string? Sha256 = null);

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

    // Long timeout on a separate client, not the 5s one CheckAsync uses — an installer is a few tens of
    // MB, not a version-check ping, and a slow connection shouldn't time out mid-download.
    private static readonly HttpClient DownloadHttp = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Downloads the installer from <paramref name="info"/>.DownloadUrl to a temp file, verifies
    /// it against Sha256 when one was published, then hands it to Windows to run (UseShellExecute — same
    /// UAC elevation prompt a manual double-click would trigger, nothing new; PrivilegesRequired=admin is
    /// set in CartridgeOS.iss). Throws on any failure (network, hash mismatch, launch) — the caller is
    /// expected to catch and fall back to just opening ReleaseUrl in a browser, same as before this
    /// existed, rather than leaving the user stuck with no path forward.</summary>
    public static async Task DownloadAndRunInstallerAsync(UpdateInfo info)
    {
        if (string.IsNullOrEmpty(info.DownloadUrl)) throw new InvalidOperationException("No direct download URL published for this release.");

        string tempPath = Path.Combine(Path.GetTempPath(), $"CartridgeOS-Setup-{info.Version}.exe");

        await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        await using (var responseStream = await DownloadHttp.GetStreamAsync(info.DownloadUrl))
        {
            await responseStream.CopyToAsync(fileStream);
        }

        if (!string.IsNullOrEmpty(info.Sha256))
        {
            await using var verifyStream = File.OpenRead(tempPath);
            byte[] actual = await SHA256.HashDataAsync(verifyStream);
            string actualHex = Convert.ToHexString(actual);
            if (!string.Equals(actualHex, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempPath);
                throw new InvalidOperationException("Downloaded installer failed hash verification.");
            }
        }

        Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
    }
}
