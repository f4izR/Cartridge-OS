using System.Diagnostics;
using System.IO;
using System.Windows;
using CartridgeOS.Core.Ipc;
using Hardcodet.Wpf.TaskbarNotification;

namespace CartridgeOS.Tray;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _trayIcon = (TaskbarIcon)Resources["TrayIcon"];
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void OpenLauncher_Click(object sender, RoutedEventArgs e)
    {
        string? launcherExe = FindLauncherExe();
        if (launcherExe is null) return; // ponytail: no error UI for a missing launcher — add if this becomes a real support issue
        Process.Start(new ProcessStartInfo(launcherExe) { UseShellExecute = true });
    }

    private async void ServiceStatus_Click(object sender, RoutedEventArgs e)
    {
        var response = await new CartridgeOsPipeClient().SendAsync(new PipeRequest("GetGameCount"));

        string message = response is { Success: true }
            ? $"Service is running. {response.Payload} game(s) in the library."
            : "Service is not running or not responding.";

        _trayIcon?.ShowBalloonTip("Cartridge OS", message, BalloonIcon.Info);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _trayIcon?.Dispose();
        Shutdown();
    }

    private static string? FindLauncherExe()
    {
        const string exeName = "CartridgeOS.Launcher.exe";

        // Production layout: installer places every exe in the same folder.
        string sameDir = Path.Combine(AppContext.BaseDirectory, exeName);
        if (File.Exists(sameDir)) return sameDir;

        // ponytail: dev-only fallback — sibling project's bin output, mirrors this exe's own path. Delete once there's an installer.
        string devPath = Path.Combine(AppContext.BaseDirectory.Replace("CartridgeOS.Tray", "CartridgeOS.Launcher"), exeName);
        return File.Exists(devPath) ? devPath : null;
    }
}
