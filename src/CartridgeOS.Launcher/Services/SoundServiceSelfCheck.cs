using System.IO;
using System.Media;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-sound`.
/// Exits 0 on pass, 1 on fail. Doesn't verify anything audible (no way to check that
/// headlessly) — just that all WAV files exist and parse as valid PCM SoundPlayer can load.
/// </summary>
public static class SoundServiceSelfCheck
{
    public static bool Run()
    {
        string soundsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");
        foreach (var fileName in new[] { "nav.wav", "confirm.wav", "tab.wav" })
        {
            string path = Path.Combine(soundsDir, fileName);
            if (!File.Exists(path)) return false;

            try
            {
                using var player = new SoundPlayer(path);
                player.Load(); // throws if the WAV is missing/malformed
            }
            catch (Exception)
            {
                return false;
            }
        }
        return true;
    }
}
