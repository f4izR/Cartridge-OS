using CartridgeOS.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CartridgeOS.Launcher.ViewModels;

/// <summary>
/// One tile in the Home carousel, tracking a single game for as long as that game stays in the
/// visible window (see MainViewModel.RefreshHomeCarouselSlots) — Game is fixed at construction;
/// only Offset (its distance from the center slot) changes as the selection moves, which is what
/// HomeView animates to make the tile visibly slide/resize into its new spot.
/// </summary>
public sealed partial class HomeCarouselSlot : ViewModelBase
{
    public GameTileViewModel Game { get; }

    [ObservableProperty]
    private int _offset;

    public bool IsCenter => Offset == 0;

    public HomeCarouselSlot(GameTileViewModel game, int offset)
    {
        Game = game;
        _offset = offset;
    }

    partial void OnOffsetChanged(int value) => OnPropertyChanged(nameof(IsCenter));
}
