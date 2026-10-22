using Tack.Core.Geometry;
using Tack.Core.Snips;

namespace Tack.Windows.Capture;

/// <summary>The two looks at the screen a snip's origin may take, and where its print flies from.</summary>
static class Looks
{
    /// <summary>
    /// Wait before the second look, if the first found nothing: Snipping
    /// Tool's overlay may still be fading out when the capture arrives.
    /// </summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Long side of the rectangle a print flies from when its picture was not
    /// found on screen, in physical px at 100% (scaled with the monitor).
    /// </summary>
    const double FallbackSide = 96.0;

    /// <summary>
    /// Takes the quick look (<paramref name="look"/> with true), and if it found
    /// nothing, or something too plain to tell, the whole one (false); before
    /// that, <paramref name="wait"/> if nothing was found at all. The second
    /// look's answer wins, else the first's stands.
    /// </summary>
    public static (Found? Found, int Tries) Take(Func<bool, Found?> look, Action wait)
    {
        var found = look(true);
        if (found is { } quick && quick.Detail >= OriginSearch.Plain)
        {
            return (found, 1);
        }
        if (found is null)
        {
            wait();
        }
        return (look(false) ?? found, 2);
    }

    /// <summary>
    /// Where a <paramref name="w"/> x <paramref name="h"/> picture's print flies
    /// from: where it was found, else a small rectangle centred on the pointer,
    /// on a monitor scaled by <paramref name="scale"/>.
    /// </summary>
    public static Rect FlyFrom(Found? found, (int X, int Y) pointer, int w, int h, double scale) =>
        found is { } f
            ? f.Rect
            : OriginSearch.AroundPointer(pointer, w, h, (int)Math.Round(FallbackSide * scale, MidpointRounding.AwayFromZero));
}
