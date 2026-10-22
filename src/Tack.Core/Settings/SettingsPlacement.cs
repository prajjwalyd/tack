using Tack.Core.Geometry;
using Tack.Core.Reveal;

namespace Tack.Core.Settings;

/// <summary>
/// Where the Settings window goes: on the monitor the board is down on,
/// centred across its work area and under the board, which it holds down
/// while it has the focus and so must not cover.
/// </summary>
public static class SettingsPlacement
{
    /// <summary>The width of the window's page area, logical px.</summary>
    public const double Width = 520;

    /// <summary>The height of the page area when there is room, logical px; the page scrolls inside it.</summary>
    public const double Height = 640;

    /// <summary>Between the board's strip and the window, and between the window and the bottom of the work area, logical px.</summary>
    public const double Gap = 16;

    /// <summary>The shortest the page area is made to fit under the board, logical px.</summary>
    public const double MinHeight = 420;

    /// <summary>
    /// The window's outer rectangle, physical px, for a monitor whose work
    /// area (the taskbar left out) is <paramref name="work"/> and whose DPI
    /// over 96 is <paramref name="scale"/>. <paramref name="chrome"/> is the
    /// window's frame (title bar and borders) at that scale.
    /// </summary>
    /// <remarks>
    /// 520 wide (less on a narrow monitor), 640 tall but cut to what is left
    /// below the board, and not under <see cref="MinHeight"/> unless the whole
    /// work area is shorter. If that is not left below the board, the window
    /// moves up over the board's lower part rather than off the screen.
    /// </remarks>
    public static Rect For(Rect work, double scale, (int W, int H) chrome)
    {
        int Px(double logical) => (int)Math.Round(logical * scale, MidpointRounding.AwayFromZero);
        var w = Math.Min(Px(Width) + chrome.W, work.W);
        var x = work.X + (work.W - w) / 2;
        var top = work.Y + Px(View.StripHeight + Gap);
        var limit = work.Y + work.H - Px(Gap);
        var h = Math.Min(Math.Max(Math.Min(Px(Height) + chrome.H, limit - top), Px(MinHeight) + chrome.H), work.H);
        var y = top + h > limit ? Math.Max(limit - h, work.Y) : top;
        return new Rect(x, y, w, h);
    }
}
