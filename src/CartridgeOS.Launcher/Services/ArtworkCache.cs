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

    /// <summary>Copies a user-picked image (tile artwork or Home background) into this app's own storage so
    /// it keeps working if the user later moves/deletes the original file they picked it from — same "custom"
    /// folder ArtworkCropWindow already writes cropped results into. Called for every direct file pick;
    /// ArtworkCropWindow's own output is already a copy, so it doesn't need to go through this too.</summary>
    public static string CopyIntoStorage(string sourcePath)
    {
        string dir = Path.Combine(CacheDir, "custom");
        Directory.CreateDirectory(dir);
        string destPath = Path.Combine(dir, $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
        File.Copy(sourcePath, destPath);
        return destPath;
    }

    /// <summary>Deletes every decoded-size variant cached for a source path (tile width, background
    /// width, any future width) — called when a game is removed so its cache entries don't linger
    /// forever (see production-readiness.md's "artwork cache doesn't grow unbounded" item). Globs by
    /// hash prefix rather than a specific width so this doesn't need updating if a new decode width
    /// is ever added elsewhere.</summary>
    public static void PurgeCacheFor(string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(CacheDir)) return;
        string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(sourcePath)));
        foreach (string file in Directory.GetFiles(CacheDir, $"{hash}_*.png"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
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
