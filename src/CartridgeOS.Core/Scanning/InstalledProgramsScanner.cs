using Microsoft.Win32;

namespace CartridgeOS.Core.Scanning;

public readonly record struct InstalledProgram(string DisplayName, string Publisher, string InstallLocation);

/// <summary>
/// Walks the standard Windows "Add/Remove Programs" registry entries. Every traditional
/// (non-Store) installed program has one of these — this is the one detection mechanism
/// GOG/Ubisoft/EA/Battle.net installs all reliably share, unlike Steam/Epic which have
/// their own documented manifest files.
/// </summary>
public sealed class InstalledProgramsScanner
{
    private static readonly string[] UninstallKeyPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public IEnumerable<InstalledProgram> Scan()
    {
        foreach (var keyPath in UninstallKeyPaths)
        {
            using var uninstallKey = Registry.LocalMachine.OpenSubKey(keyPath);
            if (uninstallKey is null) continue;

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var subKey = uninstallKey.OpenSubKey(subKeyName);
                if (subKey is null) continue;

                string? name = subKey.GetValue("DisplayName") as string;
                string? installLocation = subKey.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installLocation)) continue;

                string publisher = subKey.GetValue("Publisher") as string ?? string.Empty;
                yield return new InstalledProgram(name, publisher, installLocation);
            }
        }
    }
}
