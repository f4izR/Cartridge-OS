using System.Diagnostics;
using System.Windows.Input;
using CartridgeOS.Core;
using CartridgeOS.Launcher.Input;
using CommunityToolkit.Mvvm.Input;

namespace CartridgeOS.Launcher.ViewModels;

public sealed class PowerMenuViewModel : ViewModelBase
{
    public ICommand TurnOffSystemCommand { get; }
    public ICommand RestartSystemCommand { get; }
    public ICommand MinimizeCommand { get; }
    public ICommand ExitToDesktopCommand { get; }
    public ICommand ShutDownCartridgeOsCommand { get; }

    /// <summary>Per-brand glyph for the controller's Confirm/Back buttons (e.g. Xbox "A"/"B" vs PlayStation
    /// "✕"/"○") — captured once at menu-open time rather than kept live, since this menu is only ever open
    /// briefly (unlike the in-game overlay, which can sit open across a controller swap).</summary>
    public string ConfirmLabel { get; }
    public string BackLabel { get; }

    public PowerMenuViewModel(Action onExitToDesktop, Action onShutDownCartridgeOs, Action onMinimize, ControllerKind? controller)
    {
        TurnOffSystemCommand = new RelayCommand(TurnOffSystem);
        RestartSystemCommand = new RelayCommand(RestartSystem);
        MinimizeCommand = new RelayCommand(onMinimize);
        ExitToDesktopCommand = new RelayCommand(onExitToDesktop);
        ShutDownCartridgeOsCommand = new RelayCommand(onShutDownCartridgeOs);

        ConfirmLabel = ControllerGlyphs.Label(controller ?? ControllerKind.Keyboard, GamepadAction.Confirm);
        BackLabel = ControllerGlyphs.Label(controller ?? ControllerKind.Keyboard, GamepadAction.Back);
    }

    // ponytail: shutdown.exe ships with every Windows install — no ExitWindowsEx P/Invoke needed for a one-shot power action.
    private static void TurnOffSystem() => Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true });

    private static void RestartSystem() => Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { UseShellExecute = false, CreateNoWindow = true });
}
