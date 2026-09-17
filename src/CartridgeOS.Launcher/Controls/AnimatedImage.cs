using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace CartridgeOS.Launcher.Controls;

/// <summary>Plays a local animated GIF or WebP (SteamGridDB's animated hero art — see ArtworkFetcher) by
/// decoding frames with SkiaSharp and swapping Source as each one's turn comes up. Neither format has a
/// built-in WPF/WIC decoder — WebP has none at all (animated or static), and while WPF's own
/// GifBitmapDecoder can read GIF, it hands back each frame's raw undecoded region rather than the
/// composited frame, which flickers/tears for any GIF that uses partial-frame disposal (most do). SkiaSharp's
/// SKCodec composites both correctly and covers both formats through one code path, which is why it's used
/// for GIF here too, not just WebP.
///
/// Decodes a rolling window of frames on a background thread instead of the whole animation up front — a
/// long animation (confirmed live: a 241-frame webp hero, ~8s at ~30fps) pre-decoded entirely would hold
/// every frame in memory at once (241 * ~4.7MB at 1920x620 ≈ 1.1GB for that one animation alone), which is
/// exactly what a memory-conscious app showing a background image shouldn't do. FrameBufferCapacity bounds
/// memory to roughly that many frames regardless of how long the source animation actually is — a bounded
/// BlockingCollection gives the producer natural backpressure (it blocks once the buffer's full, so it can
/// never race ahead and bloat memory) with no extra bookkeeping needed.
///
/// Self-contained lifecycle, driven entirely by IsVisibleChanged rather than anything the owning window has
/// to remember to call: decoding stops and every buffered frame is dropped the instant this control isn't
/// actually on screen — a Hide()'d launcher window (a game running, see App.LaunchGame) or a destroyed one
/// (closed to tray) both flip IsVisible false for their whole content tree, this included. Re-decoding a
/// small cached file from disk when it becomes visible again is cheap, and paying that cost again is the
/// point: a game running shouldn't leave this holding decoded frames in memory for the whole session, any
/// more than sitting closed in tray should.</summary>
public sealed class AnimatedImage : Image
{
    // ~4.7MB/frame at a typical 1920x620 hero → ~70MB ceiling per animation, independent of how many total
    // frames the source has. Tunable if a real animation turns out to need more headroom to stay smooth.
    private const int FrameBufferCapacity = 15;

    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath), typeof(string), typeof(AnimatedImage), new PropertyMetadata(null, OnSourcePathChanged));

    public string? SourcePath
    {
        get => (string?)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    private readonly record struct DecodedFrame(BitmapSource Bitmap, TimeSpan Delay);

    private CancellationTokenSource? _cts;

    public AnimatedImage()
    {
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) Reload();
            else Release();
        };
    }

    private static void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AnimatedImage)d;
        // While hidden, just drop whatever's running rather than reload for a path nobody can see yet —
        // the next IsVisibleChanged (true) picks up the latest SourcePath on its own.
        if (control.IsVisible) control.Reload();
        else control.Release();
    }

    private void Reload()
    {
        Release();
        string? path = SourcePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        var buffer = new BlockingCollection<DecodedFrame>(FrameBufferCapacity);

        // Producer: decodes ahead of playback into the bounded buffer, blocking (backpressure — this is
        // the actual memory cap) once it's full. Runs on the thread pool — decoding is genuinely slow CPU
        // work (confirmed live: doing this inline on the UI thread stalled the carousel's own slide
        // animation for a couple of seconds on selection).
        _ = Task.Run(() => DecodeIntoBuffer(path, buffer, cts.Token), cts.Token);

        // Consumer: plain async loop instead of a DispatcherTimer — simpler to keep in lockstep with a
        // producer that can occasionally block, and every await naturally resumes back on this Window's
        // Dispatcher (WPF's SynchronizationContext), which is what makes setting Source from here safe.
        _ = PlaybackLoopAsync(buffer, cts.Token);
    }

    private async Task PlaybackLoopAsync(BlockingCollection<DecodedFrame> buffer, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                DecodedFrame frame;
                try
                {
                    frame = await Task.Run(() => buffer.Take(ct), ct);
                }
                catch (InvalidOperationException)
                {
                    return; // producer finished and the buffer's drained — a single-still-frame source, not a real animation
                }

                Source = frame.Bitmap;
                await Task.Delay(frame.Delay, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    // internal, not private: AnimatedImageSelfCheck calls DecodeFirstFrameForTest (below) directly rather
    // than fabricating a real multi-frame GIF/WebP file (SkiaSharp's public API has no animated encoder to
    // generate one with) — it round-trips a plain single-frame PNG through the same per-frame decode/copy
    // logic instead, which still exercises the highest-risk code here: the unsafe pixel copy and
    // BGRA/PBGRA channel handling.
    private static void DecodeIntoBuffer(string path, BlockingCollection<DecodedFrame> buffer, CancellationToken ct)
    {
        try
        {
            using var stream = new SKFileStream(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null) return; // corrupt/partial cache file, or a format Skia itself can't read

            var frameInfos = codec.FrameInfo;
            int frameCount = Math.Max(1, frameInfos.Length); // 0 frames means "not actually animated" — decode as a single still frame

            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var scratch = new SKBitmap(info);
            int priorFrame = -1; // Skia's own sentinel for "no prior frame to reuse"

            while (true)
            {
                for (int i = 0; i < frameCount; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var options = new SKCodecOptions(i, priorFrame);
                    var result = codec.GetPixels(info, scratch.GetPixels(), options);
                    if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                    {
                        priorFrame = -1;
                        continue; // skip a frame Skia couldn't decode rather than aborting the whole animation
                    }

                    var bitmap = CopyToWriteableBitmap(scratch, info);
                    int durationMs = frameInfos.Length > i ? frameInfos[i].Duration : 0;
                    // A 0/near-0 duration is a common encoder quirk meaning "use the viewer's default," not
                    // "no delay" — every major browser treats that as 100ms, matched here.
                    var delay = durationMs > 10 ? TimeSpan.FromMilliseconds(durationMs) : TimeSpan.FromMilliseconds(100);

                    buffer.Add(new DecodedFrame(bitmap, delay), ct); // blocks here once the buffer's full — the actual memory cap
                    priorFrame = i;
                }

                if (frameCount <= 1) return; // a single still frame — nothing to loop, PlaybackLoopAsync shows it and stops on its own
                priorFrame = -1; // don't composite the new loop's frame 0 off the previous loop's last frame
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or NotSupportedException) { }
        finally
        {
            buffer.CompleteAdding();
        }
    }

    /// <summary>Decodes just the first frame of a file — used by AnimatedImageSelfCheck to verify the
    /// pixel-copy/channel-order logic without needing a real animated fixture. Returns null on any decode
    /// failure (mirrors DecodeIntoBuffer's tolerance, just without a buffer/loop to feed).</summary>
    internal static BitmapSource? DecodeFirstFrameForTest(string path)
    {
        using var stream = new SKFileStream(path);
        using var codec = SKCodec.Create(stream);
        if (codec is null) return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var scratch = new SKBitmap(info);
        var result = codec.GetPixels(info, scratch.GetPixels(), new SKCodecOptions(0, -1));
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) return null;

        return CopyToWriteableBitmap(scratch, info);
    }

    /// <summary>Decodes the first frame of a file (static image, or an animated GIF/WebP's frame 0 — same
    /// "just the first frame" approach as DecodeFirstFrameForTest) and applies a Gaussian blur to it via
    /// Skia's own native filter, once, rather than live — used for the Home background's blurred letterbox
    /// fill. A previous version blurred the actual live/animated content every frame via a WPF
    /// VisualBrush+BlurEffect, which re-rendered ~30 times a second while an animated hero played and was
    /// confirmed live as the cause of switching feeling sluggish. A blurred backdrop doesn't need to track
    /// per-frame motion to read as ambient — baking the blur into one static bitmap, refreshed only when the
    /// selection actually changes (the same cadence as the sharp art itself), drops the ongoing cost to
    /// "draw a plain image" instead of "re-blur a live visual on every frame."</summary>
    internal static BitmapSource? DecodeBlurredFirstFrame(string path, float sigma)
    {
        using var stream = new SKFileStream(path);
        using var codec = SKCodec.Create(stream);
        if (codec is null) return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var scratch = new SKBitmap(info);
        var result = codec.GetPixels(info, scratch.GetPixels(), new SKCodecOptions(0, -1));
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) return null;

        using var surface = SKSurface.Create(info);
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp) };
        surface.Canvas.DrawBitmap(scratch, 0, 0, paint);
        using var blurredImage = surface.Snapshot();
        using var blurredBitmap = SKBitmap.FromImage(blurredImage);

        return CopyToWriteableBitmap(blurredBitmap, info);
    }

    private static WriteableBitmap CopyToWriteableBitmap(SKBitmap scratch, SKImageInfo info)
    {
        var writeable = new WriteableBitmap(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null);
        writeable.Lock();
        // Row-by-row rather than one big blit — Skia's RowBytes and WPF's BackBufferStride both happen to
        // equal Width*4 for a tightly-packed 32bpp buffer in practice, but nothing guarantees they match
        // exactly, and a single MemoryCopy across the whole buffer only produces the right image when they do.
        unsafe
        {
            int rowBytes = Math.Min(scratch.RowBytes, writeable.BackBufferStride);
            byte* src = (byte*)scratch.GetPixels();
            byte* dst = (byte*)writeable.BackBuffer;
            for (int row = 0; row < info.Height; row++)
            {
                Buffer.MemoryCopy(src + (long)row * scratch.RowBytes, dst + (long)row * writeable.BackBufferStride, rowBytes, rowBytes);
            }
        }
        writeable.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
        writeable.Unlock();
        writeable.Freeze(); // decoded off the UI thread — Freeze is what makes handing it back safe

        return writeable;
    }

    /// <summary>Cancels decoding/playback and drops the buffer — see the class comment for why this runs any
    /// time the control isn't actually on screen, not only on final teardown. The producer's `buffer.Add`
    /// unblocks and throws OperationCanceledException as soon as this fires, even if it was sitting blocked
    /// on a full buffer; nothing here waits for it to actually finish tearing down.</summary>
    private void Release()
    {
        _cts?.Cancel();
        _cts = null;
        Source = null;
    }
}
