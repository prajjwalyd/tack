using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Prints;
using Tack.Core.Pinboard;
using Tack.Core.Prints;

namespace Tack.App.Pinboard.Model;

/// <summary>
/// One print on the board as the board has it: the app's print, how it hangs
/// (tilt, pin colour, place in the row) and what it is doing. UI thread only.
/// </summary>
sealed class BoardItem(Print print, PrintView view)
{
    public string Id => Print.Id;

    /// <summary>The app's print, swapped for the new copy on each update.</summary>
    public Print Print { get; set; } = print;

    /// <summary>The print's layers on screen.</summary>
    public PrintView View { get; } = view;

    /// <summary>The resting tilt, degrees, set once when it arrives.</summary>
    public double Tilt { get; set; }

    public PinColor PinColor { get; set; }

    /// <summary>Its pin colour has been seen: on a clash between neighbours, the one not settled changes.</summary>
    public bool Settled { get; set; }

    /// <summary>The print's size, the paper border included, logical px.</summary>
    public double W { get; set; }

    public double H { get; set; }

    /// <summary>Its top-left in the row, relative to the board's centre and the screen edge.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>Unpinned, on its way off the board; no longer in the row's order.</summary>
    public bool Leaving { get; set; }

    /// <summary>Where it stood in the row's order when it was unpinned; -1 until then.</summary>
    public int IndexWhenRemoved { get; set; } = -1;

    /// <summary>A new capture's flight onto the board, until it has flown or been dropped.</summary>
    public Flight? Flight { get; set; }

    /// <summary>When the board got it, ms on the monotonic clock: the newest of a backlog when <see cref="PinnedAt"/> ties.</summary>
    public long AddedAt { get; } = Environment.TickCount64;

    /// <summary>When it was first pinned, ms since the Unix epoch.</summary>
    public long PinnedAt => Print.PinnedAt;
}
