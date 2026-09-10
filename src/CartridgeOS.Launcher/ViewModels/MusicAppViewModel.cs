using System.Windows.Media;
using CartridgeOS.Core;
using CartridgeOS.Core.Models;
using CartridgeOS.Launcher.Services;

namespace CartridgeOS.Launcher.ViewModels;

/// <summary>A user-added "quick launch" music app (Settings > Home > Music Apps) shown on Home's music
/// row whenever nothing is currently playing — see MainViewModel.MusicApps and HomeView.xaml.</summary>
public sealed class MusicAppViewModel(MusicAppEntry entry) : ViewModelBase
{
    public MusicAppEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string ExecutablePath => Entry.ExecutablePath;
    public ImageSource? Icon { get; } = AppIconExtractor.Extract(entry.ExecutablePath);
}
