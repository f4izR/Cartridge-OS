namespace CartridgeOS.Launcher.ViewModels;

/// <summary>One position in the Home carousel — the game showing there, and whether it's the center
/// (selected, enlarged) slot. Plain computed data, deliberately not tied to any Selector/ScrollViewer —
/// see MainViewModel.HomeCarouselSlots for why.</summary>
public sealed record HomeCarouselSlot(GameTileViewModel Game, bool IsCenter);
