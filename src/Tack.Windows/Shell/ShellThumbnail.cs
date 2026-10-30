using Tack.Core.Diagnostics;
using Tack.Core.Thumbnails;
using Tack.Windows.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace Tack.Windows.Shell;

/// <summary>
/// The picture Explorer shows for a file or folder: its thumbnail where
/// Windows can draw one (a document's first page, a video's frame), else its
/// icon. Drawn by the shell's own image factory on a single-threaded thread
/// of its own, since a thumbnail handler may take its time or hang: past
/// <see cref="Patience"/> it is given up for the icon, and past that again
/// for nothing. A few are drawn at once. Safe to call from any thread.
/// </summary>
public static class ShellThumbnail
{
    /// <summary>The long side of a file's thumbnail, px.</summary>
    public const int Side = 320;

    /// <summary>How long one try may take before it is given up.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(4);

    /// <summary>At most this many shell threads draw at once, so a drop of 20 files can't start 20.</summary>
    static readonly SemaphoreSlim s_drawing = new(ThumbnailRules.MaxDecodes);

    /// <summary>The file's thumbnail as JPEG, at most <see cref="Side"/> px a side, or its icon; null if Windows drew neither in time.</summary>
    public static async Task<Thumb?> MakeAsync(string path)
    {
        var picture = await TryAsync(path, iconOnly: false).ConfigureAwait(false)
            ?? await TryAsync(path, iconOnly: true).ConfigureAwait(false);
        if (picture is null)
        {
            return null;
        }
        var jpeg = await PictureEncoder.JpegAsync(picture, Side, ThumbnailRules.ThumbQuality).ConfigureAwait(false);
        return new Thumb(jpeg, picture.Width, picture.Height, null);
    }

    /// <summary>One try on a shell thread of its own; null if it failed or took too long (the thread is left to finish alone).</summary>
    static async Task<Picture?> TryAsync(string path, bool iconOnly)
    {
        await s_drawing.WaitAsync().ConfigureAwait(false);
        try
        {
            var drawn = new TaskCompletionSource<Picture?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => drawn.TrySetResult(Draw(path, iconOnly))) { IsBackground = true, Name = "tack-thumbnail" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (await Task.WhenAny(drawn.Task, Task.Delay(Patience)).ConfigureAwait(false) != drawn.Task)
            {
                DevTrace.Write($"thumbnail: {Path.GetFileName(path)} took too long{(iconOnly ? " for its icon" : string.Empty)}");
                return null;
            }
            return await drawn.Task.ConfigureAwait(false);
        }
        finally
        {
            s_drawing.Release();
        }
    }

    /// <summary>Asks the shell for the picture; runs on the shell thread.</summary>
    static unsafe Picture? Draw(string path, bool iconOnly)
    {
        IShellItemImageFactory* factory = null;
        HBITMAP bitmap = default;
        try
        {
            PInvoke.SHCreateItemFromParsingName(Path.GetFullPath(path), null, out factory).ThrowOnFailure();
            var flags = SIIGBF.SIIGBF_RESIZETOFIT | (iconOnly ? SIIGBF.SIIGBF_ICONONLY : 0);
            factory->GetImage(new SIZE(Side, Side), flags, &bitmap);
            return Pixels(bitmap);
        }
        catch (Exception e)
        {
            DevTrace.Write($"thumbnail: none for {Path.GetFileName(path)}{(iconOnly ? " (icon)" : string.Empty)}: {e.Message}");
            return null;
        }
        finally
        {
            if (!bitmap.IsNull)
            {
                PInvoke.DeleteObject(bitmap);
            }
            if (factory != null)
            {
                factory->Release();
            }
        }
    }

    /// <summary>The bitmap's pixels as an opaque picture on white; null for an empty one.</summary>
    static unsafe Picture? Pixels(HBITMAP bitmap)
    {
        BITMAP header;
        if (PInvoke.GetObject(bitmap, sizeof(BITMAP), &header) == 0 || header.bmWidth <= 0 || header.bmHeight == 0)
        {
            return null;
        }
        var (width, height) = (header.bmWidth, Math.Abs(header.bmHeight));
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                // Negative: rows top-down, as a picture keeps them.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = (uint)BI_COMPRESSION.BI_RGB,
            },
        };
        var bgra = new byte[width * height * 4];
        var screen = PInvoke.GetDC(HWND.Null);
        try
        {
            fixed (byte* bits = bgra)
            {
                if (PInvoke.GetDIBits(screen, bitmap, 0, (uint)height, bits, &info, DIB_USAGE.DIB_RGB_COLORS) == 0)
                {
                    return null;
                }
            }
        }
        finally
        {
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
        PixelOps.PremultipliedOnWhite(bgra);
        return new Picture(width, height, bgra);
    }
}
