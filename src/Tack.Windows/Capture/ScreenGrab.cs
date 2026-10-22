using System.Diagnostics;
using Tack.Core.Geometry;
using Tack.Core.Snips;
using Tack.Windows.Display;
using Tack.Windows.Imaging;

namespace Tack.Windows.Capture;

/// <summary>
/// Where on screen a Snipping Tool capture was taken, so its print can fly up
/// to the board from there. <see cref="Find"/> grabs the screen around the
/// pointer and lets <see cref="OriginSearch"/> pick the rectangle that shows
/// the picture, in physical pixels throughout. The grab is compared in memory
/// and freed at once; it is never saved, logged or sent anywhere.
/// </summary>
public static class ScreenGrab
{
    /// <summary>
    /// Finds where on screen <paramref name="picture"/> was taken, with the
    /// pointer at <paramref name="pointer"/> (physical px) when the capture
    /// arrived. Blocks for up to two looks and the wait between them, so call
    /// it off the UI thread. Works whatever the calling thread's DPI awareness.
    /// </summary>
    public static Lookup Find(Picture picture, (int X, int Y) pointer)
    {
        var started = Stopwatch.GetTimestamp();
        var cpuBefore = ThreadCpu.Used();
        var (found, tries, scale) = ThreadDpi.PerMonitorAware(() =>
        {
            var (found, tries) = Looks.Take(quick => Look(picture, pointer, quick), () => Thread.Sleep(Looks.RetryAfter));
            return (found, tries, Monitors.At(pointer.X, pointer.Y).Scale);
        });
        var rect = Looks.FlyFrom(found, pointer, picture.Width, picture.Height, scale);
        var cpu = ThreadCpu.Used() - cpuBefore;
        return new Lookup(found, rect, tries, cpu > TimeSpan.Zero ? cpu : TimeSpan.Zero, Stopwatch.GetElapsedTime(started));
    }

    /// <summary>
    /// The screen inside <paramref name="area"/> (physical px) as a picture, as
    /// Snipping Tool would capture it: for debug tools that simulate a snip.
    /// Null if the area is empty or the screen cannot be read.
    /// </summary>
    public static unsafe Picture? GrabPicture(Rect area) =>
        ThreadDpi.PerMonitorAware(() => ScreenCopy.With(area, grab =>
        {
            var bgra = new byte[area.W * area.H * 4];
            for (var y = 0; y < area.H; y++)
            {
                for (var x = 0; x < area.W; x++)
                {
                    var rgb = grab.Get(area.X + x, area.Y + y)!.Value;
                    var at = (y * area.W + x) * 4;
                    (bgra[at], bgra[at + 1], bgra[at + 2], bgra[at + 3]) = (rgb.B, rgb.G, rgb.R, 255);
                }
            }
            return new Picture(area.W, area.H, bgra);
        }));

    /// <summary>
    /// Runs <paramref name="run"/> with the calling thread DPI unaware, where
    /// Windows scales coordinates to 96 DPI: for debug tools that check the
    /// lookup still works in physical pixels.
    /// </summary>
    public static T AsDpiUnaware<T>(Func<T> run) => ThreadDpi.Unaware(run);

    /// <summary>
    /// One look at the screen. Reading the screen costs more the more is read,
    /// so a quick look reads only a square around the pointer and compares each
    /// candidate by the part of the picture inside it; otherwise the whole
    /// picture is compared.
    /// </summary>
    static Found? Look(Picture picture, (int X, int Y) pointer, bool quick)
    {
        var monitor = Monitors.At(pointer.X, pointer.Y).Bounds;
        var (w, h) = (picture.Width, picture.Height);
        var candidates = OriginSearch.Candidates(pointer, w, h, WindowFrames.Under(pointer), monitor);
        if (quick)
        {
            if (OriginSearch.FocusArea(pointer, OriginSearch.FocusHalf).Intersect(monitor) is not { } focus)
            {
                return null;
            }
            return ScreenCopy.With(focus, grab => OriginSearch.FindEach(grab, candidates, c =>
                OriginSearch.Focus(pointer, c.Rect, OriginSearch.FocusHalf) is { } region ? SamplesOf(picture, region) : null));
        }
        if (OriginSearch.SearchArea(candidates)?.Intersect(monitor) is not { } area)
        {
            return null;
        }
        var whole = SamplesOf(picture, new Rect(0, 0, w, h));
        return ScreenCopy.With(area, grab => OriginSearch.Find(whole, grab, candidates));
    }

    /// <summary>Samples <paramref name="region"/> of the picture (its own coordinates) for comparing.</summary>
    static Samples SamplesOf(Picture picture, Rect region) =>
        Samples.Within(picture.Width, picture.Height, region, picture.RgbAt);
}
