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
    private static readonly Lazy<SoundPlayer> TabSound = new(() => Load("tab.wav"));

    /// <summary>Settings-driven mutes (MainViewModel.NavigationSoundEnabled/ConfirmSoundEnabled) — plain
    /// static flags since every call site here is a static method with no viewmodel in reach. Tab-switch
    /// reuses the same NavigationSoundEnabled flag as regular nav — it's the same "moving around the UI"
    /// category, not worth its own separate Settings toggle.</summary>
    public static bool NavigateEnabled { get; set; } = true;
    public static bool ConfirmEnabled { get; set; } = true;

    public static void PlayNavigate() => Play(NavigateSound, NavigateEnabled);
    public static void PlayConfirm() => Play(ConfirmSound, ConfirmEnabled);
    public static void PlayTabSwitch() => Play(TabSound, NavigateEnabled);

    private static SoundPlayer Load(string fileName)
    {
        var player = new SoundPlayer(Path.Combine(SoundsDir, fileName));
        player.LoadAsync(); // pre-buffer so the first Play() call isn't the one paying the disk-read cost
        return player;
    }

    private static void Play(Lazy<SoundPlayer> sound, bool enabled)
    {
        if (!enabled) return;
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
