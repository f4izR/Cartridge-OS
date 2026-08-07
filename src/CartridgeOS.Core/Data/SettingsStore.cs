using System.Text.Json;
using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Data;

/// <summary>Small enough (a couple of fields) that a JSON file beats standing up another SQLite table.</summary>
public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CartridgeOS", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new AppSettings(); // missing/corrupt file — start fresh rather than crash the launcher
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
    }
}
