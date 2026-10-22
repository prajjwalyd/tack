using Tack.Core.Geometry;
using Tack.Core.Reveal;

namespace Tack.Windows.Edge;

/// <summary>Where the pointer may go while the board is down, and what sends it back up. Pure, over the view.</summary>
internal static class BoardZone
{
    /// <summary>
    /// The board (and the tip under it while that shows), with
    /// <see cref="EdgeTiming.LeaveMargin"/> around it. Above the board counts as
    /// inside: the zone stretches up to the monitor's top edge, because the
    /// pointer tends to drift back up the way it came. Null without a board.
    /// </summary>
    public static Rect? Leave(View view)
    {
        if (view.ZoneOnScreen() is not { } board)
        {
            return null;
        }
        var zone = board.Inflate(Px(EdgeTiming.LeaveMargin, view.Scale));
        var top = Math.Min(zone.Y, view.MonitorTop);
        return zone with { Y = top, H = zone.Y + zone.H - top };
    }

    /// <summary>
    /// The window's whole width from the monitor's top down through the board
    /// area: fresh from the edge the pointer may be anywhere along the top, so
    /// all of it counts until the pointer finds the board.
    /// </summary>
    public static Rect FreshStrip(View view) => new(
        view.WindowPos.X,
        view.MonitorTop,
        view.WindowSize.W,
        view.WindowPos.Y - view.MonitorTop + Px(View.StripHeight, view.Scale));

    /// <summary>
    /// A full-screen app in front sends the board back up: the edge's and a new
    /// capture's board, though Snipping Tool's overlay only the edge's, since a
    /// new capture's board may well come down while that overlay is still
    /// fading out. A full-screen app also wins over the Settings hold, whoever
    /// revealed the board (but not Snipping Tool's own overlay).
    /// </summary>
    public static bool FullScreenTucks(View view)
    {
        if (!view.Fullscreen)
        {
            return false;
        }
        var byReason = view.Reason switch
        {
            RevealReason.Edge => true,
            RevealReason.New => !view.Snipping,
            _ => false,
        };
        return byReason || (view.SettingsHeld && !view.Snipping);
    }

    static int Px(double logical, double scale) => (int)Math.Round(logical * scale, MidpointRounding.AwayFromZero);
}
