using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-artwork`.
/// Exits 0 on pass, 1 on fail. Not a unit test framework — just enough to catch a broken cache/decode path.
/// </summary>
public static class ArtworkCacheSelfCheck
{
    public static bool Run()
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), $"cartridgeos-selfcheck-{Guid.NewGuid():N}.png");
        try
        {
            SaveSolidColorPng(sourcePath, 400, 400, Colors.CornflowerBlue);

            var first = ArtworkCache.LoadAsync(sourcePath, 100).GetAwaiter().GetResult();
            if (first is null || first.PixelWidth != 100) return false;

            string cachePath = ArtworkCache.GetCachePath(sourcePath, 100);
            if (!File.Exists(cachePath)) return false;
            var cacheWriteTimeAfterFirstLoad = File.GetLastWriteTimeUtc(cachePath);

            var second = ArtworkCache.LoadAsync(sourcePath, 100).GetAwaiter().GetResult();
            if (second is null || second.PixelWidth != 100) return false;
            if (File.GetLastWriteTimeUtc(cachePath) != cacheWriteTimeAfterFirstLoad) return false; // should've hit cache, not re-encoded

            var missing = ArtworkCache.LoadAsync(Path.Combine(Path.GetTempPath(), "does-not-exist.png"), 100).GetAwaiter().GetResult();
            if (missing is not null) return false;

            File.Delete(cachePath);
            return true;
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
        }
    }

    private static void SaveSolidColorPng(string path, int width, int height, Color color)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(color), null, new System.Windows.Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
