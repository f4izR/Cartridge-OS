using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CartridgeOS.Core.Models;
using CartridgeOS.Launcher.Input;

namespace CartridgeOS.Launcher;

/// <summary>
/// Fullscreen ambient slideshow + background music, shown by App once idle (see App.CheckIdle) or via
/// Settings' "Preview Now" (App.ShowScreenSaverNow). Any input dismisses it. Bundled assets come from
/// Assets/ScreenSaver/{Images,Sound} (Content, copied to the output dir — see the .csproj) rather than
/// being embedded, since these are large media files, not small UI icons; a user-configured folder
/// (AppSettings.ScreenSaverImagesFolder/MusicFolder) replaces the bundled set entirely when set.
/// </summary>
public partial class ScreenSaverWindow : Window, IGamepadInputTarget
{
    private const string DefaultImagesFolder = "Assets/ScreenSaver/Images";
    private const string DefaultSoundFolder = "Assets/ScreenSaver/Sound";
    private static readonly TimeSpan ImageDuration = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan VolumeFadeInterval = TimeSpan.FromMilliseconds(50);
    private const double VolumeStep = 0.04; // ~1.25s full fade at the interval above
    private const int ImageDecodePixelWidth = 1920; // slideshow source photos can be much larger than this — decode down once, not per-frame

    private readonly double _targetVolume;
    private readonly List<string> _images;
    private readonly List<string> _sounds;
    private readonly DispatcherTimer _slideTimer;
    private readonly DispatcherTimer _volumeFadeTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly MediaPlayer _player = new();
    private int _imageIndex;
    private int _soundIndex;
    private bool _showingA = true;
    private bool _fadingIn;
    private bool _closing;
    private Point? _lastMousePosition;

    /// <summary>Fired once, the instant any input dismisses this window — before its own fade-out finishes
    /// closing it. Lets App close every monitor's blackout window together with this one, instead of only
    /// this window reacting to the input that actually dismissed it.</summary>
    public event Action? Dismissed;

    public ScreenSaverWindow(AppSettings settings)
    {
        InitializeComponent();

        _targetVolume = settings.ScreenSaverVolume;
        _images = LoadShuffled(settings.ScreenSaverImagesFolder ?? Path.Combine(AppContext.BaseDirectory, DefaultImagesFolder), "*.jpg");
        _sounds = LoadShuffled(settings.ScreenSaverMusicFolder ?? Path.Combine(AppContext.BaseDirectory, DefaultSoundFolder), "*.mp3");

        _slideTimer = new DispatcherTimer { Interval = ImageDuration };
        _slideTimer.Tick += (_, _) => AdvanceImage();

        _volumeFadeTimer = new DispatcherTimer { Interval = VolumeFadeInterval };
        _volumeFadeTimer.Tick += VolumeFadeTick;

        _player.Volume = 0;
        _player.MediaEnded += (_, _) => AdvanceSound();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();

        Loaded += (_, _) =>
        {
            Start();
            UpdateClock();
            _clockTimer.Start();
            ((App)Application.Current).SetModalGamepadTarget(this);
        };
        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            ((App)Application.Current).SetModalGamepadTarget(null);
        };

        PreviewKeyDown += (_, _) => Dismiss();
        PreviewMouseDown += (_, _) => Dismiss();
        PreviewMouseMove += OnPreviewMouseMove;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = $"{now:h:mm} {(now.Hour < 12 ? "AM" : "PM")}";
        DateText.Text = now.ToString("d MMMM, yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<string> LoadShuffled(string folder, string searchPattern)
    {
        if (!Directory.Exists(folder)) return [];

        var files = Directory.GetFiles(folder, searchPattern);
        // Fisher-Yates — cheap, no external shuffle helper needed for a handful of files.
        for (int i = files.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (files[i], files[j]) = (files[j], files[i]);
        }
        return [.. files];
    }

    private void Start()
    {
        if (_images.Count > 0)
            ImageA.Source = LoadImage(_images[0]);

        if (_sounds.Count > 0)
        {
            _player.Open(new Uri(_sounds[0]));
            _player.Play();
        }

        if (_images.Count > 1) _slideTimer.Start();

        _fadingIn = true;
        _volumeFadeTimer.Start();
    }

    private static BitmapImage LoadImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.DecodePixelWidth = ImageDecodePixelWidth;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Cross-dissolve: the incoming image fades in while the outgoing one fades out at the same
    /// rate, so which Image element happens to be "on top" in the visual tree never matters.</summary>
    private void AdvanceImage()
    {
        if (_images.Count == 0) return;

        _imageIndex = (_imageIndex + 1) % _images.Count;
        var incoming = _showingA ? ImageB : ImageA;
        var outgoing = _showingA ? ImageA : ImageB;
        incoming.Source = LoadImage(_images[_imageIndex]);

        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeDuration) { EasingFunction = ease });
        outgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, FadeDuration) { EasingFunction = ease });
        _showingA = !_showingA;
    }

    private void AdvanceSound()
    {
        if (_sounds.Count == 0) return;

        _soundIndex++;
        if (_soundIndex >= _sounds.Count)
        {
            _soundIndex = 0;
            Shuffle(_sounds); // reshuffle each time the playlist loops, so the order isn't identical every cycle
        }
        _player.Open(new Uri(_sounds[_soundIndex]));
        _player.Play();
    }

    private static void Shuffle(List<string> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private void VolumeFadeTick(object? sender, EventArgs e)
    {
        if (_fadingIn)
        {
            _player.Volume = Math.Min(_targetVolume, _player.Volume + VolumeStep);
            if (_player.Volume >= _targetVolume) { _volumeFadeTimer.Stop(); _fadingIn = false; }
        }
        else
        {
            _player.Volume = Math.Max(0, _player.Volume - VolumeStep);
            if (_player.Volume <= 0)
            {
                _volumeFadeTimer.Stop();
                _player.Stop();
                Close();
            }
        }
    }

    /// <summary>Only counts as activity past a small pixel threshold — otherwise mouse jitter/trackpad
    /// noise would dismiss this the instant it appears, before anyone actually meant to interact.</summary>
    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_lastMousePosition is not { } last) { _lastMousePosition = position; return; }
        if ((position - last).Length > 8) Dismiss();
    }

    /// <summary>internal, not private: App calls this when a blackout window on another monitor was the
    /// one actually dismissed, so this window still gets its own fade-out instead of an abrupt cutoff.</summary>
    internal void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        Dismissed?.Invoke();

        _slideTimer.Stop();
        _fadingIn = false;
        _volumeFadeTimer.Start(); // fades the music out, then closes the window itself once it reaches 0 — see VolumeFadeTick
    }

    public void HandleAction(GamepadAction action) => Dismiss();

    public void HandleRightStick(float x, float y)
    {
        if (Math.Abs(x) > 0.3f || Math.Abs(y) > 0.3f) Dismiss();
    }
}
