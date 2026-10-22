using Tack.Core.Geometry;

namespace Tack.Core.Reveal;

/// <summary>
/// The board on screen: whether it is revealed, why, where its window is and
/// where the pointer may go before it tucks away. Plain state shared by the
/// pointer poller, the board window and the reveal logic, under the app's lock.
/// </summary>
public sealed class View
{
    /// <summary>The board area at the top of the window (board, frame and prints), logical px.</summary>
    public const double StripHeight = 176.0;

    /// <summary>Revealed, or about to be. Cleared the moment a tuck starts, though the window stays up for the slide.</summary>
    public bool Shown { get; set; }

    /// <summary>Bumped on every reveal and tuck, so a late hide never hides a board revealed again meanwhile.</summary>
    public ulong Generation { get; set; }

    public RevealReason Reason { get; set; } = RevealReason.Edge;

    /// <summary>Revealed by the hotkey or the tray: the leave timer waits until the pointer has been over the board once.</summary>
    public bool StayOpen { get; set; }

    /// <summary>End of a new screenshot's peek, on the monotonic clock; after it the board tucks unless the pointer came over it.</summary>
    public TimeSpan? PeekUntil { get; set; }

    /// <summary>The pointer has been over the board since it was revealed.</summary>
    public bool Entered { get; set; }

    /// <summary>After a tuck the pointer has to leave the top edge before the edge can bring the board back.</summary>
    public bool EdgeArmed { get; set; } = true;

    /// <summary>The window's top-left on screen and its size, physical px.</summary>
    public (int X, int Y) WindowPos { get; set; }

    public (int W, int H) WindowSize { get; set; }

    /// <summary>Top of the monitor (not the work area): the leave zone reaches up to it.</summary>
    public int MonitorTop { get; set; }

    /// <summary>Monitor DPI / 96.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>The board, relative to the window, physical px.</summary>
    public Rect? BoardRect { get; set; }

    /// <summary>The one-time tip hanging under the board, relative to the window. Clickable like the board.</summary>
    public Rect? Tip { get; set; }

    /// <summary>Id of the print being dragged out, if any.</summary>
    public string? Dragging { get; set; }

    /// <summary>Revealed for something carried to the top edge from another app, so it can be dropped on.</summary>
    public bool DragIn { get; set; }

    public bool MenuOpen { get; set; }

    /// <summary>Settings has the keyboard focus and holds the board down, so changes show live.</summary>
    public bool SettingsHeld { get; set; }

    /// <summary>Whether the window currently lets clicks through.</summary>
    public bool Ignoring { get; set; } = true;

    /// <summary>A full-screen app is in front.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>What is in front is Snipping Tool's own overlay: it keeps the edge from revealing, but never holds back a new capture's board.</summary>
    public bool Snipping { get; set; }

    /// <summary>
    /// Starts a tuck: the new generation, or null if the board is not shown.
    /// Everything that kept it open is forgotten, the Settings hold too: a tuck
    /// is the user's say or a full-screen app's.
    /// </summary>
    public ulong? Tuck()
    {
        if (!Shown)
        {
            return null;
        }
        Shown = false;
        Generation++;
        StayOpen = false;
        PeekUntil = null;
        EdgeArmed = false;
        Tip = null;
        SettingsHeld = false;
        return Generation;
    }

    /// <summary>
    /// Settings gains (<paramref name="on"/>) or loses the keyboard focus. Returns
    /// whether the board has to be revealed for it: held, but tucked. Letting go
    /// tucks nothing here: the poller's usual rules decide.
    /// </summary>
    public bool HoldForSettings(bool on)
    {
        SettingsHeld = on;
        return on && !Shown;
    }

    public Rect? BoardOnScreen() => BoardRect is { } r ? OnScreen(r) : null;

    public Rect? TipOnScreen() => Tip is { } r ? OnScreen(r) : null;

    /// <summary>Whether the point (screen coordinates) is on the board or its tip: where the window takes clicks.</summary>
    public bool Hit(int x, int y) =>
        BoardOnScreen()?.Contains(x, y) == true || TipOnScreen()?.Contains(x, y) == true;

    /// <summary>The board and its tip together, in screen coordinates.</summary>
    public Rect? ZoneOnScreen() => (BoardOnScreen(), TipOnScreen()) switch
    {
        ({ } board, { } tip) => board.Union(tip),
        (var board, var tip) => board ?? tip,
    };

    Rect OnScreen(Rect r) => r with { X = WindowPos.X + r.X, Y = WindowPos.Y + r.Y };
}
