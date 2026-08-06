using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace CartridgeOS.Launcher.Services;

/// <summary>
/// Decodes artwork at a fixed pixel width (never full-res) and caches the
/// decoded copy to disk, keyed by source path + width, so repeat loads skip decoding.
/// </summary>
public static class ArtworkCache
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CartridgeOS", "ArtworkCache");

    public static async Task<BitmapImage?> LoadAsync(string sourcePath, int decodePixelWidth)
    {
        if (!File.Exists(sourcePath)) return null;

        Directory.CreateDirectory(CacheDir);
        string cachePath = GetCachePath(sourcePath, decodePixelWidth);

        try
        {
            if (!File.Exists(cachePath) || File.GetLastWriteTimeUtc(cachePath) < File.GetLastWriteTimeUtc(sourcePath))
                await Task.Run(() => ResizeAndSave(sourcePath, cachePath, decodePixelWidth)).ConfigureAwait(false);

            return await Task.Run(() => LoadFrozen(cachePath)).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null; // unsupported/corrupt image format
        }
    }

    public static string GetCachePath(string sourcePath, int decodePixelWidth)
    {
        string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(sourcePath)));
        return Path.Combine(CacheDir, $"{hash}_{decodePixelWidth}.png");
    }

    private static void ResizeAndSave(string sourcePath, string cachePath, int decodePixelWidth)
    {
        var decoded = LoadFrozen(sourcePath, decodePixelWidth);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(decoded));
        using var fs = File.Create(cachePath);
        encoder.Save(fs);
    }

    private static BitmapImage LoadFrozen(string path, int decodePixelWidth = 0)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze(); // required to hand the bitmap across threads
        return bitmap;
    }
}
