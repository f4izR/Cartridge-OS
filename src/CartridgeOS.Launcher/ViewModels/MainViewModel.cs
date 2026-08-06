using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CartridgeOS.Core;
using CartridgeOS.Core.Data;
using CartridgeOS.Core.Models;
using CartridgeOS.Core.Scanning;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CartridgeOS.Launcher.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const int MaxRecentGames = 10;
    private static readonly TimeSpan RescanInterval = TimeSpan.FromMinutes(15); // ponytail: hardcoded until there's a settings screen to make it configurable

    private readonly GameDatabase _db;
    private readonly DispatcherTimer _rescanTimer;

    public ObservableCollection<GameTileViewModel> Games { get; } = [];
    public ObservableCollection<GameTileViewModel> RecentGames { get; } = [];

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

    public ICommand AddGameCommand { get; }
    public ICommand ScanForGamesCommand { get; }
    public ICommand FindMoreGamesCommand { get; }

    public MainViewModel()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CartridgeOS", "games.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _db = new GameDatabase(dbPath);
        AddGameCommand = new RelayCommand(AddGame);
        ScanForGamesCommand = new RelayCommand(ScanForGames);
        FindMoreGamesCommand = new RelayCommand(async () => await FindMoreGamesAsync());

        SeedIfEmpty(_db); // ponytail: placeholder titles (some with fake play history) until the game scanner (V2) exists, delete this once real games populate the db

        foreach (var game in _db.GetAllGames())
            Games.Add(new GameTileViewModel(game));

        SelectedGame = Games.FirstOrDefault();
        RebuildRecentGames();

        _rescanTimer = new DispatcherTimer { Interval = RescanInterval };
        _rescanTimer.Tick += async (_, _) => await RescanInBackgroundAsync();
        _rescanTimer.Start();
    }

    public void StopBackgroundRescanning() => _rescanTimer.Stop();

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
            if (existingExePaths.Contains(game.ExecutablePath)) continue;

            game.Id = _db.AddGame(game);
            Games.Add(new GameTileViewModel(game));
        }
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
