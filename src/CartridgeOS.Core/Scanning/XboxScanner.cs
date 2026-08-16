using System.Diagnostics;
using System.Text.Json;
using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Scanning;

/// <summary>
/// Finds Xbox app / PC Game Pass / Microsoft Store games. Unlike every other scanner here,
/// these are UWP/MSIX packages — no registry entry, no simple manifest file to read directly —
/// so this shells out to PowerShell's Get-AppxPackage (the standard, documented way to enumerate
/// them) rather than reimplementing package-manager logic via WinRT interop.
/// </summary>
/// <remarks>
/// Filtered by MicrosoftGame.config presence (see Script) rather than a hand-maintained app-name
/// blocklist, but still a heuristic — it can miss pre-GDK legacy UWP games — so callers must let the
/// user confirm before adding anything found here — don't wire this into the trusted auto-add scan.
/// </remarks>
public sealed class XboxScanner
{
    // -not IsFramework/-not IsResourcePackage/SignatureKind -ne 'System' alone still let through every
    // ordinary Store app (Paint, Sticky Notes, Weather, Copilot, Spotify, WhatsApp, ...) — there were 50+
    // of those on a real dev machine and exactly 0 real games among them. MicrosoftGame.config is the
    // actual marker the Xbox/GDK toolchain stamps into a package's install root for "this is a game" (every
    // Xbox app / PC Game Pass title ships one); requiring it turns this from an unbounded Store-app dump
    // into an actual games filter. Trade-off: pre-GDK legacy UWP games (old ports, Solitaire) lack the file
    // and are missed — false negatives here are far less annoying than 50 false positives were.
    //
    // DisplayName in the manifest is often an unresolved "ms-resource:..." reference (needs the package's
    // PRI resource file to turn into a real string) — Get-StartApps already does that resolution for
    // anything with a Start Menu entry, keyed by the same "PackageFamilyName!AppId" AppID, so prefer its
    // name and only fall back to the raw manifest value (dropping it if that's still an unresolved
    // ms-resource: string) when a package isn't in the Start Menu index for some reason.
    private const string Script = """
        $startApps = @{}
        Get-StartApps | ForEach-Object { $startApps[$_.AppID] = $_.Name }

        Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage -and $_.SignatureKind -ne 'System' } | ForEach-Object {
            $manifestPath = Join-Path $_.InstallLocation 'AppxManifest.xml'
            if (-not (Test-Path $manifestPath)) { return }
            if (-not (Test-Path (Join-Path $_.InstallLocation 'MicrosoftGame.config'))) { return }
            try {
                $manifest = [xml](Get-Content $manifestPath -ErrorAction Stop)
                $appId = $manifest.Package.Applications.Application | Select-Object -First 1 -ExpandProperty Id
                if (-not $appId) { return }

                $fullAppId = "$($_.PackageFamilyName)!$appId"
                $title = $startApps[$fullAppId]
                if (-not $title) { $title = $manifest.Package.Properties.DisplayName }
                if (-not $title -or $title.StartsWith('ms-resource:')) { return }

                [PSCustomObject]@{
                    Title = $title
                    PackageFamilyName = $_.PackageFamilyName
                    AppId = $appId
                }
            } catch { }
        } | ConvertTo-Json -Compress
        """;

    public List<Game> Scan()
    {
        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(Script);

            using var process = Process.Start(startInfo);
            if (process is null) return [];

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);

            return Parse(output);
        }
        catch (Exception)
        {
            return []; // PowerShell missing/blocked/whatever — no-op, same as every other scanner when its source isn't available
        }
    }

    public static List<Game> Parse(string json)
    {
        var games = new List<Game>();
        if (string.IsNullOrWhiteSpace(json)) return games;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // PowerShell's ConvertTo-Json emits a bare object (not a 1-element array) for exactly one result.
            var entries = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : Enumerable.Repeat(root, 1);

            foreach (var entry in entries)
            {
                string? title = GetString(entry, "Title");
                string? packageFamilyName = GetString(entry, "PackageFamilyName");
                string? appId = GetString(entry, "AppId");
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(packageFamilyName) || string.IsNullOrEmpty(appId))
                    continue;

                games.Add(new Game
                {
                    Title = title,
                    ExecutablePath = $"shell:appsFolder\\{packageFamilyName}!{appId}"
                });
            }
        }
        catch (JsonException) { }

        return games;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
