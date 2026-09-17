using System.IO;
using System.Windows.Media.Imaging;
using CartridgeOS.Launcher.Controls;
using SkiaSharp;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Run via `dotnet run --project src/CartridgeOS.Launcher -- --self-check-animated-image`.
/// Exits 0 on pass, 1 on fail.
///
/// Doesn't cover actual multi-frame animation — SkiaSharp's public API has no animated GIF/WebP encoder
/// to fabricate a real test fixture with, and hand-crafting valid LZW-compressed GIF bytes by hand is a
/// correctness risk of its own. What this does check is the highest-risk code in
/// Controls/AnimatedImage.DecodeFirstFrameForTest regardless of frame count: the unsafe row-by-row pixel
/// copy from Skia's decode buffer into a WPF WriteableBitmap, and that the BGRA8888/Pbgra32 channel order
/// actually matches (a swapped R/B channel would still "work" — no exception, no crash — just render every
/// color wrong, which only a pixel-value check like this one would catch).
/// </summary>
public static class AnimatedImageSelfCheck
{
    public static bool Run()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cartridgeos-selfcheck-{Guid.NewGuid():N}.png");
        try
        {
            SaveSolidColorPng(path, 4, 4, red: 200, green: 80, blue: 40);

            var frame = AnimatedImage.DecodeFirstFrameForTest(path);
            if (frame is null) return false;
            if (frame.PixelWidth != 4 || frame.PixelHeight != 4) return false;
            if (!frame.IsFrozen) return false; // must be safe to hand to the UI thread from anywhere

            var pixel = new byte[4];
            frame.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            // Pbgra32 byte order: B, G, R, A.
            if (pixel[2] != 200 || pixel[1] != 80 || pixel[0] != 40 || pixel[3] != 255) return false;

            return true;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void SaveSolidColorPng(string path, int width, int height, byte red, byte green, byte blue)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(new SKColor(red, green, blue, 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}
