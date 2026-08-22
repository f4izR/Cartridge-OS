using System.Diagnostics;
using System.IO;
using System.Windows.Media;

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

    // System.Windows.Media.MediaPlayer, not System.Media.SoundPlayer: SoundPlayer.Play() on a rapid repeat
    // call (holding a nav key/stick) doesn't interrupt the still-playing sound, it queues behind it — every
    // held-repeat nav sound stacked up and only started audibly once the backlog drained, instead of
    // sounding continuous with each tile switch. MediaPlayer's Stop()+Play() cuts the previous playback off
    // immediately, same "connected" feel the tile switch itself has.
    private static readonly Lazy<MediaPlayer> NavigateSound = new(() => Load("nav.wav"));
    private static readonly Lazy<MediaPlayer> ConfirmSound = new(() => Load("confirm.wav"));
    private static readonly Lazy<MediaPlayer> TabSound = new(() => Load("tab.wav"));

    /// <summary>Settings-driven mutes (MainViewModel.NavigationSoundEnabled/ConfirmSoundEnabled/
    /// TabSwitchSoundEnabled) — plain static flags since every call site here is a static method with no
    /// viewmodel in reach.</summary>
    public static bool NavigateEnabled { get; set; } = true;
    public static bool ConfirmEnabled { get; set; } = true;
    public static bool TabSwitchEnabled { get; set; } = true;

    public static void PlayNavigate() => Play(NavigateSound, NavigateEnabled);
    public static void PlayConfirm() => Play(ConfirmSound, ConfirmEnabled);
    public static void PlayTabSwitch() => Play(TabSound, TabSwitchEnabled);

    private static MediaPlayer Load(string fileName)
    {
        var player = new MediaPlayer();
        player.Open(new Uri(Path.Combine(SoundsDir, fileName)));
        return player;
    }

    private static void Play(Lazy<MediaPlayer> sound, bool enabled, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (!enabled) { Debug.WriteLine($"[Sound] {caller}: skipped, disabled in Settings"); return; }
        try
        {
            var player = sound.Value;
            player.Stop(); // resets Position to 0 — forces an immediate restart instead of Play() no-op'ing on an already-playing clip
            player.Play();
            Debug.WriteLine($"[Sound] {caller}: played");
        }
        catch (Exception ex)
        {
            // ponytail: a missing/corrupt sound file must never break navigation or launching.
            Debug.WriteLine($"[Sound] {caller}: threw {ex.GetType().Name}: {ex.Message}");
        }
    }
}
