using System.IO;
using System.Media;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Plays short UI sound effects for navigation/launch feedback.
/// </summary>
/// <remarks>
/// ponytail: Assets/Sounds/*.wav are procedurally-generated placeholder tones, not
/// sound-designed SFX — swap the files for real ones later, nothing here needs to change.
/// </remarks>
public static class SoundService
{
    private static readonly string SoundsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");

    private static readonly Lazy<SoundPlayer> NavigateSound = new(() => Load("nav.wav"));
    private static readonly Lazy<SoundPlayer> ConfirmSound = new(() => Load("confirm.wav"));

    public static void PlayNavigate() => Play(NavigateSound);
    public static void PlayConfirm() => Play(ConfirmSound);

    private static SoundPlayer Load(string fileName)
    {
        var player = new SoundPlayer(Path.Combine(SoundsDir, fileName));
        player.LoadAsync(); // pre-buffer so the first Play() call isn't the one paying the disk-read cost
        return player;
    }

    private static void Play(Lazy<SoundPlayer> sound)
    {
        try
        {
            sound.Value.Play();
        }
        catch (Exception)
        {
            // ponytail: a missing/corrupt sound file must never break navigation or launching.
        }
    }
}
