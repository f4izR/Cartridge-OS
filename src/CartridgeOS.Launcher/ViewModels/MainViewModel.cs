using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CartridgeOS.Core;
using CartridgeOS.Core.Data;
using CartridgeOS.Core.Models;
using CartridgeOS.Core.Scanning;
using CartridgeOS.Launcher.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CartridgeOS.Launcher.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const int MaxRecentGames = 10;
    private const int MaxScanDirectories = 5; // MRU cap for "Find More Games" scan directories
    private const int BackgroundDecodeWidth = 1920; // full-screen backdrop, not a tile — decode much wider than GameTileViewModel's 200px
    private static readonly TimeSpan RescanInterval = TimeSpan.FromMinutes(15); // ponytail: hardcoded until there's a settings screen to make it configurable
    private static readonly DateTime ProcessStartTime = Process.GetCurrentProcess().StartTime; // "session" = this app run, not this particular window instance (which can be destroyed/recreated)

    // Windows' own connectivity-check endpoint (what the system tray Wi-Fi flyout itself uses under the hood) —
    // NetworkInterface.GetIsNetworkAvailable() only proves an adapter is up, not that it can actually reach the
    // internet (e.g. still associated to a router whose own WAN/ISP link is down), so IsOnline needs a real probe.
    private const string ConnectivityProbeUrl = "http://www.msftconnecttest.com/connecttest.txt";
    private static readonly TimeSpan ConnectivityProbeInterval = TimeSpan.FromSeconds(5);
    private static readonly HttpClient ConnectivityHttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly GameDatabase _db;
    private readonly AppSettings _settings = SettingsStore.Load();
    private readonly DispatcherTimer _rescanTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _connectivityTimer;
    private readonly Dispatcher _dispatcher;
    private bool _isInternetReachable = true; // last real probe result — refined every ConnectivityProbeInterval, see ProbeConnectivityAsync

    public ObservableCollection<GameTileViewModel> Games { get; } = [];
    public ObservableCollection<GameTileViewModel> RecentGames { get; } = [];

    /// <summary>Filtered view of <see cref="Games"/> driven by <see cref="SearchText"/> — what the grid actually binds to.</summary>
    public ICollectionView GamesView { get; }

    private bool _hasRecentGames;
    public bool HasRecentGames
    {
        get => _hasRecentGames;
        private set => SetProperty(ref _hasRecentGames, value);
    }

    // On-screen error toast — the fullscreen kiosk shell has no title bar/status bar for a background
    // failure to show up in, so without this, things like a scanner throwing or a DB write failing were
    // either fully silent or (worse) took the whole app down with nothing but a generic Windows crash
    // dialog. See ShowError below for how this gets populated.
    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    private bool _hasErrorMessage;
    public bool HasErrorMessage
    {
        get => _hasErrorMessage;
        private set => SetProperty(ref _hasErrorMessage, value);
    }

    private int _errorToastToken; // bumped on every new ShowError so an older auto-dismiss can't clear a newer message

    private double _storageUsedPercent;
    /// <summary>Percent used on whichever drive SelectedStorageDrive points at (system drive by default).</summary>
    public double StorageUsedPercent
    {
        get => _storageUsedPercent;
        private set => SetProperty(ref _storageUsedPercent, value);
    }

    private string _storageUsedLabel = "";
    public string StorageUsedLabel
    {
        get => _storageUsedLabel;
        private set => SetProperty(ref _storageUsedLabel, value);
    }

    private string _storageFreeLabel = "";
    public string StorageFreeLabel
    {
        get => _storageFreeLabel;
        private set => SetProperty(ref _storageFreeLabel, value);
    }

    /// <summary>Every ready fixed drive on this PC (e.g. "C:\", "D:\") — what the Settings storage-drive combo box binds to.</summary>
    public ObservableCollection<string> StorageDrives { get; } = [];

    private string? _selectedStorageDrive;
    /// <summary>Which drive RefreshStorageStats reads. Persisted; null (nothing chosen yet) falls back to the system drive.</summary>
    public string? SelectedStorageDrive
    {
        get => _selectedStorageDrive;
        set
        {
            if (!SetProperty(ref _selectedStorageDrive, value)) return;
            OnPropertyChanged(nameof(StorageDriveLabel));
            _settings.StorageDriveLetter = value;
            SettingsStore.Save(_settings);
            RefreshStorageStats();
        }
    }

    /// <summary>Drive letter without the trailing backslash (e.g. "D:" from "D:\") — so the Recently Played
    /// System Overview panel can label the stat with which drive it's actually showing.</summary>
    public string StorageDriveLabel => (SelectedStorageDrive ?? Path.GetPathRoot(Environment.SystemDirectory)!).TrimEnd('\\');

    public bool NavigationSoundEnabled
    {
        get => _settings.NavigationSoundEnabled;
        set
        {
            if (_settings.NavigationSoundEnabled == value) return;
            _settings.NavigationSoundEnabled = value;
            SoundService.NavigateEnabled = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    public bool ConfirmSoundEnabled
    {
        get => _settings.ConfirmSoundEnabled;
        set
        {
            if (_settings.ConfirmSoundEnabled == value) return;
            _settings.ConfirmSoundEnabled = value;
            SoundService.ConfirmEnabled = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    public bool ScreenSaverEnabled
    {
        get => _settings.ScreenSaverEnabled;
        set
        {
            if (_settings.ScreenSaverEnabled == value) return;
            _settings.ScreenSaverEnabled = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    /// <summary>Preset options for the inactivity-duration combo box — a free-text seconds/minutes field
    /// would need its own validation for no real benefit over a handful of sensible presets.</summary>
    public static IReadOnlyList<InactivityOption> InactivityOptions { get; } =
    [
        new(1, "1 minute"), new(2, "2 minutes"), new(5, "5 minutes"),
        new(10, "10 minutes"), new(15, "15 minutes"), new(30, "30 minutes"),
    ];

    public int ScreenSaverInactivityMinutes
    {
        get => _settings.ScreenSaverInactivityMinutes;
        set
        {
            if (_settings.ScreenSaverInactivityMinutes == value) return;
            _settings.ScreenSaverInactivityMinutes = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    public double ScreenSaverVolume
    {
        get => _settings.ScreenSaverVolume;
        set
        {
            if (_settings.ScreenSaverVolume == value) return;
            _settings.ScreenSaverVolume = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    /// <summary>Null = "use the bundled Assets/ScreenSaver files" (see ScreenSaverWindow) — shown in
    /// Settings as "Default" rather than a blank path.</summary>
    public string? ScreenSaverImagesFolder
    {
        get => _settings.ScreenSaverImagesFolder;
        set
        {
            if (_settings.ScreenSaverImagesFolder == value) return;
            _settings.ScreenSaverImagesFolder = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    public string? ScreenSaverMusicFolder
    {
        get => _settings.ScreenSaverMusicFolder;
        set
        {
            if (_settings.ScreenSaverMusicFolder == value) return;
            _settings.ScreenSaverMusicFolder = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    /// <summary>Empty/null means "use the bundled key" — see ArtworkFetcher.EffectiveSteamGridDbApiKey.</summary>
    public string? SteamGridDbApiKeyOverride
    {
        get => _settings.SteamGridDbApiKeyOverride;
        set
        {
            if (_settings.SteamGridDbApiKeyOverride == value) return;
            _settings.SteamGridDbApiKeyOverride = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    /// <summary>Empty/null means "use the bundled key" — see ArtworkFetcher.EffectiveTheGamesDbApiKey.</summary>
    public string? TheGamesDbApiKeyOverride
    {
        get => _settings.TheGamesDbApiKeyOverride;
        set
        {
            if (_settings.TheGamesDbApiKeyOverride == value) return;
            _settings.TheGamesDbApiKeyOverride = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    private string _sessionUptimeLabel = "";
    public string SessionUptimeLabel
    {
        get => _sessionUptimeLabel;
        private set => SetProperty(ref _sessionUptimeLabel, value);
    }

    private GameTileViewModel? _selectedGame;
    public GameTileViewModel? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (!SetProperty(ref _selectedGame, value)) return;
            OnPropertyChanged(nameof(IsContinuePlayingGameSelected));
            RefreshHomeCarouselSlots();
            _ = RefreshHomeBackgroundAsync();
        }
    }

    // How many tiles show on each side of the center one — 3+3+1 = 7 visible. Bump this for a wider
    // carousel; RefreshHomeCarouselSlots automatically shrinks it for a library smaller than that.
    // internal: HomeView.xaml.cs mirrors this to size its Canvas — keep the two in sync.
    internal const int HomeCarouselSideCount = 3;

    /// <summary>
    /// The Home carousel's visible tiles — center slot is the selected game, the rest wrap around it
    /// (modulo, matching the "infinite" Left/Right nav). Deliberately not a ListBox.SelectedItem/ScrollViewer
    /// -driven carousel: that kept silently breaking (visual-tree timing for finding the internal
    /// ScrollViewer, IsSelected trigger not visibly updating) because a Selector control isn't really built
    /// for "resize and re-center the item as you move". A slot is keyed by which game it holds, not by a
    /// fixed array position: a game that stays in the visible window just gets its Offset updated (so
    /// HomeView can animate that tile sliding/resizing to its new spot), and only games newly entering or
    /// leaving the window are added/removed. A position-keyed version (offset always mapped to the same
    /// list index) was tried first — it never actually moved, since "the center slot" was always the same
    /// container, so its IsCenter trigger never re-fired.
    /// </summary>
    public ObservableCollection<HomeCarouselSlot> HomeCarouselSlots { get; } = [];

    private void RefreshHomeCarouselSlots()
    {
        var games = GamesView.Cast<GameTileViewModel>().ToList();
        if (games.Count == 0)
        {
            HomeCarouselSlots.Clear();
            return;
        }

        int centerIndex = SelectedGame is null ? 0 : games.IndexOf(SelectedGame);
        if (centerIndex < 0) centerIndex = 0;

        int side = Math.Min(HomeCarouselSideCount, (games.Count - 1) / 2); // don't show the same game twice when the library is small

        var existingByGame = HomeCarouselSlots.ToDictionary(s => s.Game);
        var target = new HashSet<GameTileViewModel>();

        for (int offset = -side; offset <= side; offset++)
        {
            int index = ((centerIndex + offset) % games.Count + games.Count) % games.Count;
            var game = games[index];
            target.Add(game);

            if (existingByGame.TryGetValue(game, out var slot)) slot.Offset = offset;
            else HomeCarouselSlots.Add(new HomeCarouselSlot(game, offset));
        }

        for (int i = HomeCarouselSlots.Count - 1; i >= 0; i--)
        {
            if (!target.Contains(HomeCarouselSlots[i].Game)) HomeCarouselSlots.RemoveAt(i);
        }
    }

    private bool _isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    private AppScreen _selectedScreen = AppScreen.Home;
    public AppScreen SelectedScreen
    {
        get => _selectedScreen;
        set
        {
            if (!SetProperty(ref _selectedScreen, value)) return;
            OnPropertyChanged(nameof(IsHomeScreen));
            // Search is only visible on Library (see MainWindow.xaml's search pill) — clear it on leaving,
            // so it can't keep silently filtering Home's carousel / Recently Played's nav list (both walk
            // GamesView too) with no visible search box left to explain why.
            if (value != AppScreen.Library) IsSearchOpen = false;
            // Set here rather than at each input site (gamepad LB/RB via CycleScreen, mouse click on the
            // nav pill's RadioButtons) so both trigger it for free — SetProperty above already no-ops an
            // unchanged value, so this never fires on construction's initial assignment.
            SoundService.PlayTabSwitch();
        }
    }

    /// <summary>Drives the selection-follows-background behavior — deliberately Home-only; Library/Recently
    /// Played get a plain gradient background regardless of WallpaperMode or SelectedGame.</summary>
    public bool IsHomeScreen => SelectedScreen == AppScreen.Home;

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            GamesView.Refresh();
        }
    }

    private bool _isSearchOpen;
    public bool IsSearchOpen
    {
        get => _isSearchOpen;
        set
        {
            if (!SetProperty(ref _isSearchOpen, value)) return;
            if (!value) SearchText = ""; // closing the search box clears the filter rather than leaving the grid stuck filtered
        }
    }

    private int? _controllerBatteryPercent;
    /// <summary>Set by App forwarding GamepadWatcher's ControllerBatteryChanged — null when no controller reports one.</summary>
    public int? ControllerBatteryPercent
    {
        get => _controllerBatteryPercent;
        set
        {
            if (!SetProperty(ref _controllerBatteryPercent, value)) return;
            RefreshBatteryDisplay();
        }
    }

    private string _batteryGlyph = "🎮";
    public string BatteryGlyph
    {
        get => _batteryGlyph;
        private set => SetProperty(ref _batteryGlyph, value);
    }

    private string? _batteryLabel;
    /// <summary>Null when there's nothing to show — no controller battery reported and this machine has no battery of its own (desktop).</summary>
    public string? BatteryLabel
    {
        get => _batteryLabel;
        private set
        {
            if (!SetProperty(ref _batteryLabel, value)) return;
            OnPropertyChanged(nameof(HasBatteryInfo));
        }
    }

    public bool HasBatteryInfo => BatteryLabel is not null;

    private bool _isOnline = true;
    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (!SetProperty(ref _isOnline, value)) return;
            OnPropertyChanged(nameof(ConnectivityLabel));
        }
    }

    public string ConnectivityLabel => IsOnline ? "ONLINE" : "OFFLINE";

    private string _currentTimeText = "";
    public string CurrentTimeText
    {
        get => _currentTimeText;
        private set => SetProperty(ref _currentTimeText, value);
    }

    private string _currentDateText = "";
    public string CurrentDateText
    {
        get => _currentDateText;
        private set => SetProperty(ref _currentDateText, value);
    }

    public WallpaperMode WallpaperMode
    {
        get => _settings.WallpaperMode;
        set
        {
            if (_settings.WallpaperMode == value) return;
            _settings.WallpaperMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUsingGameArtworkBackground));
            SettingsStore.Save(_settings);
        }
    }

    public bool IsUsingGameArtworkBackground => WallpaperMode == WallpaperMode.SelectedGameArtwork;

    public string? CustomWallpaperPath => _settings.CustomWallpaperPath;

    /// <summary>MRU list of directories previously picked for "Find More Games" (NVIDIA-style — most-recent-first,
    /// capped at MaxScanDirectories). Backed by AppSettings.ScanDirectories; this ObservableCollection is what the
    /// Settings combo box actually binds to.</summary>
    public ObservableCollection<string> ScanDirectories { get; } = [];

    private string? _selectedScanDirectory;
    /// <summary>Which of ScanDirectories (if any) "Find More Games" should scan instead of the default
    /// Program-Files/drives sweep. Not persisted itself — only the MRU list is; this just remembers the
    /// current pick for as long as the app is open.</summary>
    public string? SelectedScanDirectory
    {
        get => _selectedScanDirectory;
        set => SetProperty(ref _selectedScanDirectory, value);
    }

    private bool _isRecursiveScan;
    /// <summary>"Scan whole drive" mode — walks the entire subtree under SelectedScanDirectory (e.g. an
    /// actual drive root like "D:\") instead of just its immediate children, so games installed a few
    /// folders deep (Riot Games\VALORANT\live\VALORANT.exe) are still found. Slower, opt-in, UI-only
    /// (not persisted) — see StandaloneExecutableScanner.ScanRecursive.</summary>
    public bool IsRecursiveScan
    {
        get => _isRecursiveScan;
        set => SetProperty(ref _isRecursiveScan, value);
    }

    private ImageSource? _customWallpaperImage;
    public ImageSource? CustomWallpaperImage
    {
        get => _customWallpaperImage;
        private set => SetProperty(ref _customWallpaperImage, value);
    }

    private ImageSource? _homeBackgroundImage;
    /// <summary>Home's full-screen backdrop — decoded at BackgroundDecodeWidth (1920), not the ~200px tile
    /// thumbnail (GameTileViewModel.Artwork), which is exactly why the background looked pixelated when it
    /// was bound directly to that instead of this.</summary>
    public ImageSource? HomeBackgroundImage
    {
        get => _homeBackgroundImage;
        private set => SetProperty(ref _homeBackgroundImage, value);
    }

    // Session-only memory of which games already had a hero-image fetch attempted, so re-selecting a game
    // with no hero (SteamGridDB has none, or it's not a Steam game SteamGridDB could resolve) doesn't hit
    // the API again every time — hero fetching is lazy/per-selection, unlike boxart which fetches eagerly
    // for the whole library, so this guard is what keeps it from re-requesting on every reselect.
    private readonly HashSet<int> _heroFetchAttempted = [];

    public ICommand AddGameCommand { get; }
    public ICommand RemoveGameCommand { get; }
    public ICommand ChangeArtworkCommand { get; }
    public ICommand RevertArtworkCommand { get; }
    public ICommand ScanForGamesCommand { get; }
    public ICommand FindMoreGamesCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand ChooseWallpaperCommand { get; }
    public ICommand ToggleSearchCommand { get; }
    public ICommand BrowseScanDirectoryCommand { get; }
    public ICommand BrowseScreenSaverImagesCommand { get; }
    public ICommand ClearScreenSaverImagesCommand { get; }
    public ICommand BrowseScreenSaverMusicCommand { get; }
    public ICommand ClearScreenSaverMusicCommand { get; }
    public ICommand DismissErrorCommand { get; }

    /// <summary>Puts a message on the on-screen error toast, auto-dismissed after a few seconds (or
    /// immediately via DismissErrorCommand). The only user-visible surface for a failure that isn't
    /// already handled some other way (a MessageBox confirmation, a tray balloon) — see field comment
    /// on ErrorMessage above for why this exists at all.</summary>
    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasErrorMessage = true;
        int token = ++_errorToastToken;
        _ = DismissErrorAfterDelayAsync(token);
    }

    private async Task DismissErrorAfterDelayAsync(int token)
    {
        await Task.Delay(TimeSpan.FromSeconds(6));
        if (token == _errorToastToken) HasErrorMessage = false;
    }

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        SoundService.NavigateEnabled = _settings.NavigationSoundEnabled;
        SoundService.ConfirmEnabled = _settings.ConfirmSoundEnabled;

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CartridgeOS", "games.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _db = new GameDatabase(dbPath);
        AddGameCommand = new RelayCommand(AddGame);
        RemoveGameCommand = new RelayCommand(RemoveGame);
        ChangeArtworkCommand = new RelayCommand(ChangeArtwork);
        RevertArtworkCommand = new RelayCommand(RevertArtwork);
        DismissErrorCommand = new RelayCommand(() => HasErrorMessage = false);
        ScanForGamesCommand = new RelayCommand(ScanForGames);
        FindMoreGamesCommand = new RelayCommand(async () => await FindMoreGamesAsync());
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        ChooseWallpaperCommand = new RelayCommand(async () => await ChooseWallpaperAsync());
        ToggleSearchCommand = new RelayCommand(() => IsSearchOpen = !IsSearchOpen);
        BrowseScanDirectoryCommand = new RelayCommand(BrowseScanDirectory);
        BrowseScreenSaverImagesCommand = new RelayCommand(() => ScreenSaverImagesFolder = BrowseForFolder("Select a folder of images for the screen saver") ?? ScreenSaverImagesFolder);
        ClearScreenSaverImagesCommand = new RelayCommand(() => ScreenSaverImagesFolder = null);
        BrowseScreenSaverMusicCommand = new RelayCommand(() => ScreenSaverMusicFolder = BrowseForFolder("Select a folder of music for the screen saver") ?? ScreenSaverMusicFolder);
        ClearScreenSaverMusicCommand = new RelayCommand(() => ScreenSaverMusicFolder = null);

        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;
        GamesView.CollectionChanged += (_, _) => RefreshHomeCarouselSlots(); // covers search-filter changes, scan results, add/remove — anything that changes what's visible or its order

        if (!string.IsNullOrEmpty(_settings.CustomWallpaperPath))
            _ = LoadCustomWallpaperAsync(_settings.CustomWallpaperPath);

        if (_settings.ScanDirectories.Count == 0)
        {
            // Every install has these — seed the MRU so first-time users see a couple of ready-to-use
            // entries instead of an empty combo box, rather than requiring a Browse... click before
            // "Find More Games" can be pointed anywhere.
            _settings.ScanDirectories.AddRange(DefaultScanDirectories().Distinct(StringComparer.OrdinalIgnoreCase));
            SettingsStore.Save(_settings);
        }
        foreach (var dir in _settings.ScanDirectories) ScanDirectories.Add(dir);
        SelectedScanDirectory = ScanDirectories.FirstOrDefault();

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            StorageDrives.Add(drive.RootDirectory.FullName);
        // Explicitly select the system drive when nothing's been chosen yet, rather than leaving this null —
        // null is the correct internal "not chosen" state for RefreshStorageStats' fallback, but a null
        // SelectedItem just shows the combo box empty even though the system drive is what's actually
        // being shown, which read as "nothing selected" even though storage stats were displaying fine.
        string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)!;
        SelectedStorageDrive = _settings.StorageDriveLetter is { } saved && StorageDrives.Contains(saved) ? saved : systemDrive;

        SeedIfEmpty(_db); // ponytail: placeholder titles (some with fake play history) until the game scanner (V2) exists, delete this once real games populate the db

        foreach (var game in _db.GetAllGames())
        {
            var tile = new GameTileViewModel(game);
            Games.Add(tile);

            if (string.IsNullOrEmpty(game.ArtworkPath))
                _ = FetchArtworkInBackgroundAsync(game, tile);
        }

        SelectedGame = Games.FirstOrDefault();
        RebuildRecentGames();

        _rescanTimer = new DispatcherTimer { Interval = RescanInterval };
        _rescanTimer.Tick += async (_, _) => { await RescanInBackgroundAsync(); RefreshStorageStats(); };
        _rescanTimer.Start();

        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        _connectivityTimer = new DispatcherTimer { Interval = ConnectivityProbeInterval };
        _connectivityTimer.Tick += (_, _) => _ = ProbeConnectivityAsync();
        _connectivityTimer.Start();
        _ = ProbeConnectivityAsync();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => UpdateClock();
        _statusTimer.Start();
        UpdateClock();
        RefreshStorageStats();
    }

    public void StopBackgroundRescanning() => _rescanTimer.Stop();

    /// <summary>Stops the clock/connectivity ticks and unsubscribes the network-change listener — must run when the window closes, same reason as <see cref="StopBackgroundRescanning"/>.</summary>
    public void StopStatusUpdates()
    {
        _statusTimer.Stop();
        _connectivityTimer.Stop();
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }

    private bool FilterGame(object obj) =>
        string.IsNullOrWhiteSpace(SearchText) || ((GameTileViewModel)obj).Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

    /// <summary>An adapter-state change is a good hint to re-probe sooner rather than wait out the full
    /// ConnectivityProbeInterval — but the event's own IsAvailable flag isn't trusted directly (see
    /// ConnectivityProbeUrl's comment above), so this just re-checks the adapter and kicks a fresh probe.</summary>
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        _dispatcher.BeginInvoke(() =>
        {
            RefreshConnectivityDisplay();
            _ = ProbeConnectivityAsync();
        });

    /// <summary>Real internet-reachability probe, not just an adapter-up check — GetIsNetworkAvailable() alone
    /// stays true if the adapter is still associated to a router that's itself lost its WAN/ISP connection.</summary>
    private async Task ProbeConnectivityAsync()
    {
        bool reachable;
        try
        {
            using var response = await ConnectivityHttpClient.GetAsync(ConnectivityProbeUrl).ConfigureAwait(false);
            reachable = response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            reachable = false;
        }

        _ = _dispatcher.BeginInvoke(() =>
        {
            _isInternetReachable = reachable;
            RefreshConnectivityDisplay();
        });
    }

    private void RefreshConnectivityDisplay() =>
        IsOnline = NetworkInterface.GetIsNetworkAvailable() && _isInternetReachable;

    private void UpdateClock()
    {
        var now = DateTime.Now;
        CurrentTimeText = $"{now:hh:mm} {(now.Hour < 12 ? "am" : "pm")}";
        CurrentDateText = now.ToString("ddd, d MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        RefreshBatteryDisplay(); // piggyback on the 1s tick to keep the device-battery fallback fresh too
        RefreshConnectivityDisplay(); // cheap (local adapter check + cached probe result), fine to run every tick

        var uptime = now - ProcessStartTime;
        SessionUptimeLabel = uptime.TotalHours >= 1 ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m" : $"{uptime.Minutes}m";
    }

    // Storage rarely changes fast enough to need second-level freshness — refreshed at startup and on
    // the existing 15-minute rescan tick rather than adding a dedicated timer for it.
    private void RefreshStorageStats()
    {
        try
        {
            string root = SelectedStorageDrive ?? Path.GetPathRoot(Environment.SystemDirectory)!;
            var drive = new DriveInfo(root);
            long usedBytes = drive.TotalSize - drive.AvailableFreeSpace;
            StorageUsedPercent = drive.TotalSize > 0 ? 100.0 * usedBytes / drive.TotalSize : 0;
            StorageUsedLabel = $"{usedBytes / 1_073_741_824.0:N0} GB used";
            StorageFreeLabel = $"{drive.AvailableFreeSpace / 1_073_741_824.0:N0} GB free";
        }
        catch (IOException)
        {
            // drive not ready/accessible — leave whatever was last shown
        }
    }

    private void RefreshBatteryDisplay()
    {
        int? percent = ControllerBatteryPercent ?? DeviceBattery.GetPercent();
        BatteryGlyph = ControllerBatteryPercent.HasValue ? "🎮" : "💻";
        BatteryLabel = percent.HasValue ? $"{percent}%" : null;
    }

    public void RecordPlayed(GameTileViewModel game)
    {
        game.MarkPlayedNow();
        _db.UpdateLastPlayed(game.Id, game.LastPlayedUtc!.Value);
        RebuildRecentGames();

        // The hero card's highlight (IsContinuePlayingGameSelected) only lights up when SelectedGame
        // matches ContinuePlayingGame — without this, RebuildRecentGames correctly moves the just-launched
        // game's content into the hero slot, but SelectedGame keeps pointing at whatever was clicked
        // before, so the hero shows the new game unhighlighted while the old selection's border stays lit
        // in the 2x2 grid below it. Reads as "it didn't move to the hero card" even though it did.
        SelectedGame = game;
    }

    /// <summary>Persists elapsed playtime and updates the same tile in-memory — called once a directly-tracked
    /// game process exits (see App.LaunchGame/OnGameExited; Steam/Xbox shell launches can't be timed this way).</summary>
    public void RecordPlaytime(GameTileViewModel game, int minutes)
    {
        _db.AddPlaytime(game.Id, minutes);
        game.AddPlaytime(minutes);
    }

    private void RebuildRecentGames()
    {
        RecentGames.Clear();
        foreach (var game in Games.Where(g => g.LastPlayedUtc.HasValue)
                                   .OrderByDescending(g => g.LastPlayedUtc)
                                   .Take(MaxRecentGames))
            RecentGames.Add(game);

        HasRecentGames = RecentGames.Count > 0;
        OnPropertyChanged(nameof(ContinuePlayingGame));
        OnPropertyChanged(nameof(HasContinuePlayingGame));
        OnPropertyChanged(nameof(OtherRecentGames));
        OnPropertyChanged(nameof(IsContinuePlayingGameSelected));
    }

    /// <summary>The single most-recently-played game — the Recently Played screen's hero card.</summary>
    public GameTileViewModel? ContinuePlayingGame => RecentGames.FirstOrDefault();
    public bool HasContinuePlayingGame => ContinuePlayingGame is not null;

    /// <summary>Drives the hero card's highlighted-gradient background — only lit up while it's actually the
    /// selected tile; selecting a different game (e.g. in the 2x2 grid below) drops it back to a plain card,
    /// same as every other tile's unselected state.</summary>
    public bool IsContinuePlayingGameSelected => SelectedGame is not null && ReferenceEquals(SelectedGame, ContinuePlayingGame);

    /// <summary>The next 4 most-recent games after the hero — fills the 2x2 grid, for 5 recently-played games total.</summary>
    public IEnumerable<GameTileViewModel> OtherRecentGames => RecentGames.Skip(1).Take(4);

    private void AddGame()
    {
        var exeDialog = new OpenFileDialog
        {
            Title = "Select game executable",
            Filter = "Executable (*.exe)|*.exe"
        };
        if (exeDialog.ShowDialog() != true) return;

        var artworkDialog = new OpenFileDialog
        {
            Title = "Select artwork (optional — cancel to skip)",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        string? artworkPath = artworkDialog.ShowDialog() == true ? artworkDialog.FileName : null;

        var game = new Game
        {
            Title = Path.GetFileNameWithoutExtension(exeDialog.FileName),
            ExecutablePath = exeDialog.FileName,
            ArtworkPath = artworkPath
        };

        try
        {
            game.Id = _db.AddGame(game);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't add \"{game.Title}\" — {ex.Message}");
            return;
        }

        var tile = new GameTileViewModel(game);
        Games.Add(tile);
        SelectedGame = tile;
    }

    private void RemoveGame()
    {
        var game = SelectedGame;
        if (game is null) return;

        var confirmed = MessageBox.Show($"Remove \"{game.Title}\" from Cartridge OS?\n\nThis only removes it from the library — the game itself isn't touched.",
            "Remove Game", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes) return;

        try
        {
            _db.DeleteGame(game.Id);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't remove \"{game.Title}\" — {ex.Message}");
            return;
        }

        Games.Remove(game);
        SelectedGame = Games.FirstOrDefault();
        RebuildRecentGames();

        // Otherwise this game's cache entries (downloaded boxart/hero originals, decoded-size variants)
        // just linger on disk forever with nothing left to ever reference or clean them up again. Best-
        // effort past this point — the game's already gone from the library either way, so a cache-purge
        // failure (e.g. a file briefly locked) isn't worth surfacing as an error to the user.
        try
        {
            ArtworkFetcher.PurgeCache(game.Title, game.Id);
            ArtworkCache.PurgeCacheFor(game.ArtworkPath);
            ArtworkCache.PurgeCacheFor(game.HeroImagePath);
        }
        catch (IOException) { }
    }

    // Tile artwork slot is ~2:3 portrait (matches ArtworkCropWindow's crop viewport, and the box-art
    // convention already used elsewhere in this app — Steam's library_600x900.jpg, SteamGridDB/TheGamesDB
    // boxart). Skip the crop step when the picked image is already close enough that WPF's automatic
    // center-fill crop (Stretch="UniformToFill" on the tile Image) won't cut off anything important.
    private const double TileAspect = 2.0 / 3.0;
    private const double AspectTolerance = 0.05;

    /// <summary>Replaces the selected game's own artwork (tile image + selected-game background) with a user-picked local file — distinct from ChooseWallpaperAsync's app-wide custom-background setting below.</summary>
    private void ChangeArtwork()
    {
        var game = SelectedGame;
        if (game is null) return;

        var dialog = new OpenFileDialog
        {
            Title = $"Choose artwork for {game.Title}",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            string artworkPath = dialog.FileName;
            if (!MatchesTileAspect(artworkPath))
            {
                var cropWindow = new ArtworkCropWindow(artworkPath) { Owner = Application.Current.MainWindow };
                if (cropWindow.ShowDialog() != true || cropWindow.ResultPath is null) return;
                artworkPath = cropWindow.ResultPath;
            }

            _db.UpdateArtworkPath(game.Id, artworkPath);
            game.SetArtworkPath(artworkPath);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or System.Data.Common.DbException)
        {
            ShowError($"Couldn't set artwork for \"{game.Title}\" — {ex.Message}");
        }
    }

    private static bool MatchesTileAspect(string imagePath)
    {
        var frame = BitmapFrame.Create(new Uri(imagePath), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        double aspect = (double)frame.PixelWidth / frame.PixelHeight;
        return Math.Abs(aspect - TileAspect) <= AspectTolerance;
    }

    /// <summary>Undoes the selected game's most recent artwork change. No-ops if there isn't one (nothing selected, or no prior change this session — reverting isn't persisted across app restarts).</summary>
    private void RevertArtwork()
    {
        var game = SelectedGame;
        if (game is null || !game.TryRevertArtwork(out var restored)) return;

        try { _db.UpdateArtworkPath(game.Id, restored); }
        catch (System.Data.Common.DbException ex) { ShowError($"Couldn't revert artwork for \"{game.Title}\" — {ex.Message}"); }
    }

    private async Task ChooseWallpaperAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a wallpaper image",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            _settings.CustomWallpaperPath = dialog.FileName;
            _settings.WallpaperMode = WallpaperMode.CustomImage;
            SettingsStore.Save(_settings);
            OnPropertyChanged(nameof(WallpaperMode));
            OnPropertyChanged(nameof(IsUsingGameArtworkBackground));
            OnPropertyChanged(nameof(CustomWallpaperPath));

            await LoadCustomWallpaperAsync(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            ShowError($"Couldn't set that wallpaper — {ex.Message}");
        }
    }

    private async Task LoadCustomWallpaperAsync(string path) =>
        CustomWallpaperImage = await ArtworkCache.LoadAsync(path, BackgroundDecodeWidth);

    /// <summary>Program Files (and x86) — present on every Windows install, so these seed the scan-directory
    /// MRU list before the user has ever browsed for one themselves. Skips a path that doesn't exist (some
    /// installs genuinely lack the x86 one) rather than adding a dead combo-box entry.</summary>
    private static IEnumerable<string> DefaultScanDirectories()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (Directory.Exists(programFiles)) yield return programFiles;
        if (Directory.Exists(programFilesX86)) yield return programFilesX86;
    }

    /// <summary>NVIDIA-style "add a directory to scan": browse once, and it joins the MRU combo box for next
    /// time — same idea as ChooseWallpaperAsync's file picker, just for a folder and a list instead of one path.</summary>
    private void BrowseScanDirectory()
    {
        var dialog = new OpenFolderDialog { Title = "Select a folder to scan for games" };
        if (dialog.ShowDialog() != true) return;
        AddScanDirectory(dialog.FolderName);
    }

    /// <summary>Adds (or re-promotes) a folder to the front of the scan-directory MRU list, persists it, and
    /// selects it — shared by the Settings picker and the scan-results window's own picker (passed down as a
    /// callback, see FindMoreGamesAsync) so both stay in sync with the same underlying list/selection.</summary>
    private void AddScanDirectory(string path)
    {
        _settings.ScanDirectories.RemoveAll(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase));
        _settings.ScanDirectories.Insert(0, path);
        if (_settings.ScanDirectories.Count > MaxScanDirectories)
            _settings.ScanDirectories.RemoveRange(MaxScanDirectories, _settings.ScanDirectories.Count - MaxScanDirectories);
        SettingsStore.Save(_settings);

        ScanDirectories.Clear();
        foreach (var dir in _settings.ScanDirectories) ScanDirectories.Add(dir);
        SelectedScanDirectory = path;
    }

    /// <summary>Plain single-folder picker, no MRU list — used by the screen saver's images/music overrides
    /// (unlike the scan-directory picker, these are a single override each, not a recall list). Returns null
    /// on cancel so callers can `?? previousValue` and leave the setting untouched.</summary>
    private static string? BrowseForFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>Redecodes the Home background at full resolution for whichever game is now selected — prefers
    /// the wide hero image once one's been fetched, falls back to a high-res decode of the portrait boxart in
    /// the meantime (or permanently, if no hero is ever found). Also kicks off a lazy hero fetch the first
    /// time each game is selected without one.</summary>
    private async Task RefreshHomeBackgroundAsync()
    {
        var game = SelectedGame;
        string? path = game?.HeroImagePath ?? game?.ArtworkPath;
        HomeBackgroundImage = string.IsNullOrEmpty(path) ? null : await ArtworkCache.LoadAsync(path, BackgroundDecodeWidth);

        if (game is not null && game.HeroImagePath is null && _heroFetchAttempted.Add(game.Id))
            _ = FetchHeroThenRefreshAsync(game);
    }

    private async Task FetchHeroThenRefreshAsync(GameTileViewModel tile)
    {
        // FetchHeroAndCacheAsync only reads Id/Title/ExecutablePath — a throwaway snapshot is simpler than
        // threading the original Core.Models.Game back through from wherever tile was first constructed.
        var snapshot = new Game { Id = tile.Id, Title = tile.Title, ExecutablePath = tile.ExecutablePath };
        string? heroPath = await ArtworkFetcher.FetchHeroAndCacheAsync(snapshot);
        if (heroPath is null) return;

        _db.UpdateHeroImagePath(tile.Id, heroPath);
        tile.SetHeroImagePath(heroPath);
        if (ReferenceEquals(SelectedGame, tile)) await RefreshHomeBackgroundAsync(); // swap the boxart-based backdrop for the real hero now that it's ready
    }

    private void ScanForGames()
    {
        var (games, failed) = ScanTrustedLauncherSources();
        ImportScannedGames(games);
        // User-initiated (clicked "Scan for Games"), so a failure here should actually be visible —
        // unlike RescanInBackgroundAsync below, which deliberately doesn't toast for the same failure.
        if (failed.Count > 0) ShowError($"Couldn't scan {string.Join(", ", failed)} — see scan.log for details.");
    }

    // Runs the same trusted-launcher scan the button does, but off the UI thread (registry/file
    // I/O shouldn't cause a periodic hitch) and never the heuristic standalone scanner — popping
    // its confirmation dialog unprompted in the background would be bad UX. Failures are logged (see
    // ScanTrustedLauncherSources) but deliberately not toasted here — a scanner that's reliably broken
    // on this machine would otherwise pop an unprompted error every 15 minutes forever.
    private async Task RescanInBackgroundAsync()
    {
        var (games, _) = await Task.Run(ScanTrustedLauncherSources);
        ImportScannedGames(games);
    }

    // Each scanner runs in isolation — one throwing (bad registry data, a malformed manifest, a
    // permissions error on some install folder) used to take the *entire* scan down with it, and
    // since nothing caught that, it would propagate all the way to App's DispatcherUnhandledException
    // and crash the whole fullscreen session over what should be a "the Ubisoft scan didn't work"
    // situation. Returns what every other scanner still found, plus which ones failed.
    private static (List<Game> Games, List<string> Failed) ScanTrustedLauncherSources()
    {
        var games = new List<Game>();
        var failed = new List<string>();

        void TryScan(string name, Func<List<Game>> scan)
        {
            try { games.AddRange(scan()); }
            catch (Exception ex) { failed.Add(name); LogScanFailure(name, ex); }
        }

        TryScan("Steam", () => new SteamScanner().Scan());
        TryScan("Epic", () => new EpicScanner().Scan());
        TryScan("Riot", () => new RiotScanner().Scan());
        TryScan("GOG", () => new PublisherGameScanner("GOG").Scan());
        TryScan("Ubisoft", () => new PublisherGameScanner("Ubisoft").Scan());
        TryScan("EA", () => new PublisherGameScanner("Electronic Arts").Scan());
        TryScan("Battle.net", () => new PublisherGameScanner("Blizzard Entertainment").Scan());

        return (games, failed);
    }

    // ponytail: plain append-to-file log, same pattern as DiscordRichPresence/ArtworkFetcher's own logs —
    // this file stays tiny (scanners essentially never throw; this exists for the rare case one does).
    private static void LogScanFailure(string scannerName, Exception ex)
    {
        try
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CartridgeOS", "scan.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {scannerName} scanner failed: {ex}\n\n");
        }
        catch (IOException) { }
    }

    // Async because XboxScanner shells out to PowerShell (can take a couple seconds) — running that
    // synchronously on the UI thread like the other heuristic scan used to would freeze the window.
    private async Task FindMoreGamesAsync()
    {
        var directoryToScan = SelectedScanDirectory;
        var recursive = IsRecursiveScan;
        var candidates = await ScanCandidatesAsync(directoryToScan, recursive);

        // No early-return on an empty first scan — the window now owns its own directory picker (see
        // ScanResultsViewModel), so opening it even with zero initial results lets the user pick a
        // different folder right there instead of bouncing back to Settings and re-clicking this button.
        var resultsViewModel = new ScanResultsViewModel(candidates, ScanDirectories, directoryToScan, recursive, ScanCandidatesAsync, AddScanDirectory);
        var window = new ScanResultsWindow { DataContext = resultsViewModel, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true)
            ImportScannedGames(resultsViewModel.SelectedGames);
    }

    /// <summary>Standalone-exe candidates not already in the library, scoped to one directory when given
    /// (replaces the default Program-Files/drives sweep entirely — once you're pointing it at where your
    /// games actually live, scanning C:\Program Files too is just noise) or the default sweep otherwise.
    /// recursive selects StandaloneExecutableScanner.ScanRecursive (walks the whole subtree, needed for
    /// games installed a few folders deep, e.g. Riot Games\VALORANT) instead of the shallow one-level Scan
    /// — meaningless (and ignored) when directory is null, since the default sweep's roots are Program
    /// Files itself, not something you'd want to recurse. XboxScanner (shell:appsFolder\... results —
    /// installed packages, not filesystem paths, so no directory could ever scope it) only runs for the
    /// default (no-directory) scan — a picked directory means "show me what's in this folder," not "also
    /// show me every Xbox/Store app regardless of where I pointed this."
    /// Shared by the initial "Find More Games" scan and the scan-results window's own re-scan-on-change.
    /// Both real sub-scanners run isolated (see ScanTrustedLauncherSources) and this is always
    /// user-initiated, so a failure toasts rather than logging quietly.</summary>
    private async Task<List<Game>> ScanCandidatesAsync(string? directory, bool recursive)
    {
        var existingExePaths = new HashSet<string>(Games.Select(g => g.ExecutablePath), StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();
        var result = await Task.Run(() =>
        {
            var scanner = new StandaloneExecutableScanner();
            List<Game> standalone;
            try
            {
                standalone = directory is null ? scanner.Scan()
                    : recursive ? scanner.ScanRecursive([directory])
                    : scanner.Scan([directory]);
            }
            catch (Exception ex) { failed.Add("folder scan"); LogScanFailure("StandaloneExecutable", ex); standalone = []; }

            IEnumerable<Game> combined = standalone;
            if (directory is null)
            {
                try { combined = standalone.Concat(new XboxScanner().Scan()); }
                catch (Exception ex) { failed.Add("Xbox/Store"); LogScanFailure("Xbox", ex); }
            }

            return combined.Where(g => !existingExePaths.Contains(g.ExecutablePath)).ToList();
        });

        if (failed.Count > 0) ShowError($"Couldn't complete {string.Join(", ", failed)} — see scan.log for details.");
        return result;
    }

    private void ImportScannedGames(IEnumerable<Game> scannedGames)
    {
        var existingExePaths = new HashSet<string>(Games.Select(g => g.ExecutablePath), StringComparer.OrdinalIgnoreCase);

        foreach (var game in scannedGames)
        {
            if (!existingExePaths.Add(game.ExecutablePath)) continue; // false = already present, including duplicates within this same scan batch

            game.Id = _db.AddGame(game);
            var tile = new GameTileViewModel(game);
            Games.Add(tile);

            if (string.IsNullOrEmpty(game.ArtworkPath))
                _ = FetchArtworkInBackgroundAsync(game, tile);
        }
    }

    // Artwork isn't required for a game to be usable, so this runs fire-and-forget after the tile
    // is already showing its placeholder rather than making the scan wait on network calls.
    private async Task FetchArtworkInBackgroundAsync(Game game, GameTileViewModel tile)
    {
        string? artworkPath = await ArtworkFetcher.FetchAndCacheAsync(game);
        if (artworkPath is null) return;

        _db.UpdateArtworkPath(game.Id, artworkPath);
        tile.SetArtworkPath(artworkPath);
    }

    private static void SeedIfEmpty(GameDatabase db)
    {
        if (db.GetAllGames().Count > 0) return;

        string[] titles = ["Half-Life 2", "Portal 2", "Hades", "Celeste", "Elden Ring", "Stardew Valley", "Hollow Knight", "Doom Eternal"];
        for (int i = 0; i < titles.Length; i++)
        {
            var game = new Game { Title = titles[i], ExecutablePath = string.Empty };
            if (i < 3) game.LastPlayedUtc = DateTime.UtcNow.AddHours(-i); // fake recent-play history for the first few, to demo the recent row
            db.AddGame(game);
        }
    }

}
