using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;

namespace Tack.App.Pinboard.Motion;

/// <summary>One flight under way: waiting on its picture, then in the air until the print takes over.</summary>
sealed class Flying(BoardItem item, Flight flight)
{
    public BoardItem Item { get; } = item;

    public Flight Flight { get; } = flight;

    /// <summary>The sharp picture, once decoded; the thumbnail stands in if it is late.</summary>
    public ImageSource? Picture { get; set; }

    /// <summary>It took off (or was found unable to): a late picture no longer matters.</summary>
    public bool Started { get; set; }

    /// <summary>Ended early: the print left, or the board tucked.</summary>
    public bool Aborted { get; set; }

    public FlightStandIn? StandIn { get; set; }

    /// <summary>Its timers: the wait on the picture, the landing, the tock, the hand-over.</summary>
    public List<DispatcherQueueTimer> Timers { get; } = [];

    /// <summary>The stand-in's motions.</summary>
    public List<Playing> Motions { get; } = [];
}
