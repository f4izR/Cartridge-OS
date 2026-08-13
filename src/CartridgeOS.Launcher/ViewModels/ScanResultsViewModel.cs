using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CartridgeOS.Core;
using CartridgeOS.Core.Models;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CartridgeOS.Launcher.ViewModels;

public sealed class ScanResultsViewModel : ViewModelBase
{
    private readonly Func<string?, bool, Task<List<Game>>> _scanAsync;
    private readonly Action<string> _addScanDirectory;

    public ObservableCollection<CandidateGameViewModel> Candidates { get; } = [];

    /// <summary>Same MRU list instance MainViewModel's Settings picker uses — picking a folder here (or
    /// there) keeps both in sync, since a folder added from either place goes through the same
    /// AddScanDirectory callback.</summary>
    public ObservableCollection<string> ScanDirectories { get; }

    public ICommand SelectAllCommand { get; }
    public ICommand UnselectAllCommand { get; }
    public ICommand BrowseScanDirectoryCommand { get; }

    private string? _selectedScanDirectory;

    /// <summary>Changing this re-scans immediately, replacing Candidates with just that folder's results —
    /// never merged with whatever the previous directory (or the initial default sweep) turned up.</summary>
    public string? SelectedScanDirectory
    {
        get => _selectedScanDirectory;
        set
        {
            if (SetProperty(ref _selectedScanDirectory, value)) _ = RescanAsync();
        }
    }

    private bool _isRecursiveScan;
    /// <summary>"Scan whole drive" — same meaning as MainViewModel.IsRecursiveScan, just its own copy here
    /// since this window can flip it independently of whatever Settings had it set to. Toggling re-scans,
    /// same as changing the directory.</summary>
    public bool IsRecursiveScan
    {
        get => _isRecursiveScan;
        set
        {
            if (SetProperty(ref _isRecursiveScan, value)) _ = RescanAsync();
        }
    }

    private bool _isScanning;
    public bool IsScanning { get => _isScanning; private set => SetProperty(ref _isScanning, value); }

    public ScanResultsViewModel(IEnumerable<Game> initialCandidates, ObservableCollection<string> scanDirectories,
        string? initialDirectory, bool initialRecursive, Func<string?, bool, Task<List<Game>>> scanAsync, Action<string> addScanDirectory)
    {
        ScanDirectories = scanDirectories;
        _scanAsync = scanAsync;
        _addScanDirectory = addScanDirectory;
        _selectedScanDirectory = initialDirectory; // no rescan here — initialCandidates already reflects this directory/mode
        _isRecursiveScan = initialRecursive;

        foreach (var game in initialCandidates) Candidates.Add(new CandidateGameViewModel(game));

        SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
        UnselectAllCommand = new RelayCommand(() => SetAllSelected(false));
        BrowseScanDirectoryCommand = new RelayCommand(BrowseScanDirectory);
    }

    public IEnumerable<Game> SelectedGames => Candidates.Where(c => c.IsSelected).Select(c => c.Game);

    private void SetAllSelected(bool selected)
    {
        foreach (var candidate in Candidates)
            candidate.IsSelected = selected;
    }

    private void BrowseScanDirectory()
    {
        var dialog = new OpenFolderDialog { Title = "Select a folder to scan for games" };
        if (dialog.ShowDialog() != true) return;

        _addScanDirectory(dialog.FolderName); // updates the shared MRU list + persists
        SelectedScanDirectory = dialog.FolderName; // triggers the rescan
    }

    private async Task RescanAsync()
    {
        IsScanning = true;
        try
        {
            var results = await _scanAsync(SelectedScanDirectory, IsRecursiveScan);
            Candidates.Clear();
            foreach (var game in results) Candidates.Add(new CandidateGameViewModel(game));
        }
        finally
        {
            IsScanning = false;
        }
    }
}
