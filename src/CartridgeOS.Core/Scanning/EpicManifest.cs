using System.Text.Json;
using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Scanning;

/// <summary>
/// Parses a single Epic Games Launcher ".item" manifest (JSON despite the extension)
/// into a Game. Pulled out of EpicScanner so the parsing logic is testable without a
/// real Epic install.
/// </summary>
public static class EpicManifest
{
    public static Game? Parse(string manifestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            var root = doc.RootElement;

            string? name = GetString(root, "DisplayName");
            string? installLocation = GetString(root, "InstallLocation");
            string? launchExecutable = GetString(root, "LaunchExecutable");

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installLocation) || string.IsNullOrEmpty(launchExecutable))
                return null;

            return new Game
            {
                Title = name,
                ExecutablePath = Path.Combine(installLocation, launchExecutable)
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
