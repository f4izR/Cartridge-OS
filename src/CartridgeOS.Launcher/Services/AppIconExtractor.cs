using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CartridgeOS.Launcher.Services;

/// <summary>Shared by MediaSessionService (icon for whatever's currently playing) and the Home "quick
/// launch" music apps row (icon for a user-added exe) — both just need an exe's own associated icon.</summary>
internal static class AppIconExtractor
{
    public static ImageSource? Extract(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}
