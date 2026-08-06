using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CartridgeOS.Core;
using CartridgeOS.Core.Models;
using CommunityToolkit.Mvvm.Input;

namespace CartridgeOS.Launcher.ViewModels;

public sealed class ScanResultsViewModel : ViewModelBase
{
    public ObservableCollection<CandidateGameViewModel> Candidates { get; }

    public ICommand SelectAllCommand { get; }
    public ICommand UnselectAllCommand { get; }

    public ScanResultsViewModel(IEnumerable<Game> candidates)
    {
        Candidates = new ObservableCollection<CandidateGameViewModel>(candidates.Select(g => new CandidateGameViewModel(g)));
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
        UnselectAllCommand = new RelayCommand(() => SetAllSelected(false));
    }

    public IEnumerable<Game> SelectedGames => Candidates.Where(c => c.IsSelected).Select(c => c.Game);

    private void SetAllSelected(bool selected)
    {
        foreach (var candidate in Candidates)
            candidate.IsSelected = selected;
    }
}
