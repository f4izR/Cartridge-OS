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
/// Pure heuristic, same as StandaloneExecutableScanner: there's no reliable local "this package
/// is a game, not some other Store app" signal, so callers must let the user confirm before
/// adding anything found here — don't wire this into the trusted auto-add scan.
/// </remarks>
public sealed class XboxScanner
{
    private const string Script = """
        Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage -and $_.SignatureKind -ne 'System' } | ForEach-Object {
            $manifestPath = Join-Path $_.InstallLocation 'AppxManifest.xml'
            if (-not (Test-Path $manifestPath)) { return }
            try {
                $manifest = [xml](Get-Content $manifestPath -ErrorAction Stop)
                $appId = $manifest.Package.Applications.Application | Select-Object -First 1 -ExpandProperty Id
                $displayName = $manifest.Package.Properties.DisplayName
                if (-not $appId -or -not $displayName) { return }
                [PSCustomObject]@{
                    Title = $displayName
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
