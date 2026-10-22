namespace Tack.Windows.Edge;

/// <summary>
/// What a tucked board knows of the pointer at the top edge: what the strips
/// said, and how long it has rested there. Pure, so the timing is tested
/// without a mouse. Times are on the monotonic clock.
/// </summary>
internal sealed class EdgeWatch
{
    /// <summary>The strips are up: what they say counts.</summary>
    bool _listening;

    /// <summary>Since when, and where, the pointer has rested at the edge.</summary>
    (TimeSpan Since, (int X, int Y) Anchor)? _rest;

    /// <summary>A strip has the pointer, until it says it left.</summary>
    public bool Arrived { get; private set; }

    /// <summary>Something carried with a button held came over a strip, at this time.</summary>
    public TimeSpan? DragSince { get; private set; }

    /// <summary>The strips come up (or go): nothing from the last time is true.</summary>
    public void Listen(bool on)
    {
        if (on)
        {
            _listening = true;
            return;
        }
        _listening = false;
        Arrived = false;
        DragSince = null;
        _rest = null;
    }

    public void Hear(EdgeEvent edgeEvent, TimeSpan now)
    {
        // Said by a strip just taken down.
        if (!_listening)
        {
            return;
        }
        switch (edgeEvent)
        {
            case EdgeEvent.Arrived:
                Arrived = true;
                break;
            case EdgeEvent.Left:
                Arrived = false;
                _rest = null;
                break;
            case EdgeEvent.DragEntered:
                DragSince = now;
                break;
            case EdgeEvent.DragLeft:
                DragSince = null;
                break;
        }
    }

    /// <summary>Not at the edge, or not to be revealed from: start over.</summary>
    public void Reset()
    {
        _rest = null;
        DragSince = null;
    }

    /// <summary>
    /// One look with the pointer at the edge and nothing against a reveal.
    /// <paramref name="free"/>: no button is held. The rest is counted from the
    /// first look, within <see cref="EdgeTiming.EdgeJitter"/> of where it was,
    /// and the pointer starts over after a reveal. A drag waits a dwell of its
    /// own, wherever it moves along the strip: it left it, if it left the edge.
    /// </summary>
    public Rest Rested(TimeSpan now, (int X, int Y) point, bool free, TimeSpan dwell)
    {
        if (DragSince is { } since)
        {
            if (now - since < Min(EdgeTiming.DragDwell, dwell))
            {
                return Rest.No;
            }
            Reset();
            return Rest.Drag;
        }
        if (!free)
        {
            _rest = null;
            return Rest.No;
        }
        if (_rest is { } rest
            && Math.Abs(point.X - rest.Anchor.X) < EdgeTiming.EdgeJitter
            && Math.Abs(point.Y - rest.Anchor.Y) < EdgeTiming.EdgeJitter)
        {
            if (now - rest.Since < dwell)
            {
                return Rest.No;
            }
            Reset();
            return Rest.Edge;
        }
        _rest = (now, point);
        return Rest.No;
    }

    /// <summary>
    /// When a strip-woken poller must look again, or null to be told. At the
    /// edge it looks every <see cref="EdgeTiming.Poll"/>, and at the end of the
    /// dwell, to the millisecond. A board just tucked under the pointer (not
    /// <paramref name="armed"/>) looks slowly for it to leave, as no strip may
    /// have seen it come; a full-screen app is asked about every so often.
    /// </summary>
    public TimeSpan? NextLook(TimeSpan now, TimeSpan dwell, bool armed, bool fullScreen)
    {
        if (!armed)
        {
            return EdgeTiming.PollSlow;
        }
        if (!Arrived && DragSince is null)
        {
            return null;
        }
        if (fullScreen)
        {
            return EdgeTiming.FullScreenCheck;
        }
        TimeSpan? atEdge = !Arrived ? null
            : _rest is { } rest ? Min(Remaining(dwell, now - rest.Since), EdgeTiming.Poll)
            : EdgeTiming.Poll;
        TimeSpan? dragged = DragSince is { } since ? Remaining(Min(EdgeTiming.DragDwell, dwell), now - since) : null;
        return (atEdge, dragged) switch
        {
            ({ } a, { } d) => Min(a, d),
            (var a, var d) => a ?? d,
        };
    }

    static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    /// <summary>What is left of <paramref name="wait"/> after <paramref name="spent"/>, never below zero.</summary>
    static TimeSpan Remaining(TimeSpan wait, TimeSpan spent) => wait > spent ? wait - spent : TimeSpan.Zero;
}
