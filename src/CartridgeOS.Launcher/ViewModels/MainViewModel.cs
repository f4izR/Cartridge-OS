using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private const int BackgroundDecodeWidth = 1920; // full-screen backdrop, not a tile — decode much wider than GameTileViewModel's 200px
    private static readonly TimeSpan RescanInterval = TimeSpan.FromMinutes(15); // ponytail: hardcoded until there's a settings screen to make it configurable

    private readonly GameDatabase _db;
    private readonly AppSettings _settings = SettingsStore.Load();
    private readonly DispatcherTimer _rescanTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly Dispatcher _dispatcher;

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

    private GameTileViewModel? _selectedGame;
    public GameTileViewModel? SelectedGame
    {
        get => _selectedGame;
        set => SetProperty(ref _selectedGame, value);
    }

    private bool _isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

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

    private ImageSource? _customWallpaperImage;
    public ImageSource? CustomWallpaperImage
    {
        get => _customWallpaperImage;
        private set => SetProperty(ref _customWallpaperImage, value);
    }

    public ICommand AddGameCommand { get; }
    public ICommand RemoveGameCommand { get; }
    public ICommand ChangeArtworkCommand { get; }
    public ICommand RevertArtworkCommand { get; }
    public ICommand ScanForGamesCommand { get; }
    public ICommand FindMoreGamesCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand ChooseWallpaperCommand { get; }
    public ICommand ToggleSearchCommand { get; }

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CartridgeOS", "games.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _db = new GameDatabase(dbPath);
        AddGameCommand = new RelayCommand(AddGame);
        RemoveGameCommand = new RelayCommand(RemoveGame);
        ChangeArtworkCommand = new RelayCommand(ChangeArtwork);
        RevertArtworkCommand = new RelayCommand(RevertArtwork);
        ScanForGamesCommand = new RelayCommand(ScanForGames);
        FindMoreGamesCommand = new RelayCommand(async () => await FindMoreGamesAsync());
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        ChooseWallpaperCommand = new RelayCommand(async () => await ChooseWallpaperAsync());
        ToggleSearchCommand = new RelayCommand(() => IsSearchOpen = !IsSearchOpen);

        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;

        if (!string.IsNullOrEmpty(_settings.CustomWallpaperPath))
            _ = LoadCustomWallpaperAsync(_settings.CustomWallpaperPath);

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
        _rescanTimer.Tick += async (_, _) => await RescanInBackgroundAsync();
        _rescanTimer.Start();

        IsOnline = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => UpdateClock();
        _statusTimer.Start();
        UpdateClock();
    }

    public void StopBackgroundRescanning() => _rescanTimer.Stop();

    /// <summary>Stops the clock tick and unsubscribes the network-change listener — must run when the window closes, same reason as <see cref="StopBackgroundRescanning"/>.</summary>
    public void StopStatusUpdates()
    {
        _statusTimer.Stop();
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }

    private bool FilterGame(object obj) =>
        string.IsNullOrWhiteSpace(SearchText) || ((GameTileViewModel)obj).Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        _dispatcher.BeginInvoke(() => IsOnline = e.IsAvailable);

    private void UpdateClock()
    {
        var now = DateTime.Now;
        CurrentTimeText = $"{now:hh:mm} {(now.Hour < 12 ? "am" : "pm")}";
        CurrentDateText = now.ToString("ddd, d MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        RefreshBatteryDisplay(); // piggyback on the 1s tick to keep the device-battery fallback fresh too
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
    }

    private void RebuildRecentGames()
    {
        RecentGames.Clear();
        foreach (var game in Games.Where(g => g.LastPlayedUtc.HasValue)
                                   .OrderByDescending(g => g.LastPlayedUtc)
                                   .Take(MaxRecentGames))
            RecentGames.Add(game);

        HasRecentGames = RecentGames.Count > 0;
    }

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
        game.Id = _db.AddGame(game);

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

        _db.DeleteGame(game.Id);
        Games.Remove(game);
        SelectedGame = Games.FirstOrDefault();
        RebuildRecentGames();
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

        _db.UpdateArtworkPath(game.Id, restored);
    }

    private async Task ChooseWallpaperAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a wallpaper image",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        if (dialog.ShowDialog() != true) return;

        _settings.CustomWallpaperPath = dialog.FileName;
        _settings.WallpaperMode = WallpaperMode.CustomImage;
        SettingsStore.Save(_settings);
        OnPropertyChanged(nameof(WallpaperMode));
        OnPropertyChanged(nameof(IsUsingGameArtworkBackground));
        OnPropertyChanged(nameof(CustomWallpaperPath));

        await LoadCustomWallpaperAsync(dialog.FileName);
    }

    private async Task LoadCustomWallpaperAsync(string path) =>
        CustomWallpaperImage = await ArtworkCache.LoadAsync(path, BackgroundDecodeWidth);

    private void ScanForGames() => ImportScannedGames(ScanTrustedLauncherSources());

    // Runs the same trusted-launcher scan the button does, but off the UI thread (registry/file
    // I/O shouldn't cause a periodic hitch) and never the heuristic standalone scanner — popping
    // its confirmation dialog unprompted in the background would be bad UX.
    private async Task RescanInBackgroundAsync()
    {
        var scanned = await Task.Run(() => ScanTrustedLauncherSources().ToList());
        ImportScannedGames(scanned);
    }

    private static IEnumerable<Game> ScanTrustedLauncherSources() =>
        new SteamScanner().Scan()
            .Concat(new EpicScanner().Scan())
            .Concat(new RiotScanner().Scan())
            .Concat(new PublisherGameScanner("GOG").Scan())
            .Concat(new PublisherGameScanner("Ubisoft").Scan())
            .Concat(new PublisherGameScanner("Electronic Arts").Scan())
            .Concat(new PublisherGameScanner("Blizzard Entertainment").Scan());

    // Async because XboxScanner shells out to PowerShell (can take a couple seconds) — running that
    // synchronously on the UI thread like the other heuristic scan used to would freeze the window.
    private async Task FindMoreGamesAsync()
    {
        var existingExePaths = new HashSet<string>(Games.Select(g => g.ExecutablePath), StringComparer.OrdinalIgnoreCase);
        var candidates = await Task.Run(() => new StandaloneExecutableScanner().Scan()
            .Concat(new XboxScanner().Scan())
            .Where(g => !existingExePaths.Contains(g.ExecutablePath))
            .ToList());

        if (candidates.Count == 0)
        {
            MessageBox.Show("No new games found.", "Cartridge OS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var resultsViewModel = new ScanResultsViewModel(candidates);
        var window = new ScanResultsWindow { DataContext = resultsViewModel, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true)
            ImportScannedGames(resultsViewModel.SelectedGames);
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
