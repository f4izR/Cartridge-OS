using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.Media.Control;

namespace CartridgeOS.Launcher.Services;

/// <summary>Wraps Windows' OS-wide "now playing" session (System Media Transport Controls) so Home can
/// show/control whatever's playing system-wide — Spotify, a browser tab, anything that reports a session —
/// without the launcher knowing about any specific app. Driven by the OS's own session/property-changed
/// events, not a poll timer.</summary>
public sealed class MediaSessionService : ObservableObject
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    private bool _hasSession;
    private string? _title;
    private string? _artist;
    private bool _isPlaying;
    private ImageSource? _appIcon;

    public bool HasSession { get => _hasSession; private set => SetProperty(ref _hasSession, value); }
    public string? Title { get => _title; private set => SetProperty(ref _title, value); }
    public string? Artist { get => _artist; private set => SetProperty(ref _artist, value); }
    public bool IsPlaying { get => _isPlaying; private set => SetProperty(ref _isPlaying, value); }
    public ImageSource? AppIcon { get => _appIcon; private set => SetProperty(ref _appIcon, value); }

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch
        {
            return; // ponytail: no SMTC support on this OS build — widget just stays hidden (HasSession false)
        }

        _manager.CurrentSessionChanged += (_, _) => Application.Current.Dispatcher.Invoke(AttachSession);
        AttachSession();
    }

    private void AttachSession()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _session = _manager?.GetCurrentSession();
        HasSession = _session is not null;
        if (_session is null)
        {
            Title = null;
            Artist = null;
            AppIcon = null;
            return;
        }

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        RefreshPlaybackInfo();
        _ = RefreshPropertiesAsync();
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        Application.Current.Dispatcher.Invoke(() => _ = RefreshPropertiesAsync());

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        Application.Current.Dispatcher.Invoke(RefreshPlaybackInfo);

    private void RefreshPlaybackInfo() =>
        IsPlaying = _session?.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    private async Task RefreshPropertiesAsync()
    {
        if (_session is not { } session) return;
        var props = await session.TryGetMediaPropertiesAsync();
        if (!ReferenceEquals(session, _session)) return; // session changed while we were awaiting

        Title = props.Title;
        Artist = props.Artist;
        AppIcon = ResolveAppIcon(session.SourceAppUserModelId);
    }

    // ponytail: only resolves an icon for classic Win32 apps that report their own exe path as the AUMID
    // (Spotify desktop and most third-party media players do) — packaged/UWP apps report a package-family
    // AUMID instead and just show no icon. Upgrade path: resolve those via Package/AppListEntry if needed.
    private static ImageSource? ResolveAppIcon(string aumid) => AppIconExtractor.Extract(aumid);

    public void TogglePlayPause() => _ = _session?.TryTogglePlayPauseAsync();
    public void Next() => _ = _session?.TrySkipNextAsync();
    public void Previous() => _ = _session?.TrySkipPreviousAsync();

    /// <summary>Called right after launching a "quick launch" music app (Home's music row) — waits for
    /// that exe to register its own SMTC session, then presses Play on it. Most desktop media apps (Spotify
    /// included) resume wherever playback last left off on their own, so "press Play" is enough to get
    /// "recently played" going again without this app knowing anything about that specific player.
    /// ponytail: fixed poll/timeout rather than a real "app started" event — SMTC exposes no such signal,
    /// and the app might not even register a session until its UI has fully loaded.</summary>
    public async Task ResumePlaybackForAsync(string exePath)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            if (_manager?.GetCurrentSession() is { } session &&
                string.Equals(session.SourceAppUserModelId, exePath, StringComparison.OrdinalIgnoreCase))
            {
                await session.TryPlayAsync();
                return;
            }
        }
    }
}
