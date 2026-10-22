using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.Core.Geometry;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// A new capture's flight: it lifts off the screen where it was taken, arcs
/// into its slot shrinking and turning to its tilt, and is pressed onto the
/// felt as the pin drives in; then the one-time tip, if asked for. A stand-in
/// on the overlay layer carries the picture until the print takes over. Under
/// reduced motion, or without a picture to fly, the print pins on plainly.
/// A tuck or the print leaving ends a flight at once.
/// </summary>
sealed class Flights(BoardContext board) : BoardPart(board)
{
    readonly Dictionary<BoardItem, Flying> _flying = [];
    readonly TipSlip _tip = new(board);

    BoardState State => Board.State;

    /// <summary>Flies <paramref name="item"/> in with its <see cref="BoardItem.Flight"/>, keeping it hidden until it takes off.</summary>
    public void FlyIn(BoardItem item)
    {
        var flight = item.Flight;
        item.Flight = null;
        var row = Board.Parts.Row;
        if (flight is null || State.Reduced || !(flight.From.W > 0 && flight.From.H > 0) || flight.Jpeg.Length == 0)
        {
            row.Unhide(item);
            Board.Parts.PrintMotions.PinOn(item);
            if (flight?.Tip == true)
            {
                Board.Timers.Later(Board.Clock.Scaled(TipMotions.ReducedDelayMs), _tip.Show);
            }
            return;
        }
        row.Hide(item);
        var flying = new Flying(item, flight);
        _flying[item] = flying;
        var wait = MotionClock.Fixed(FlightPath.DecodeWaitMs, "a wait on the picture's decoding, not choreography");
        flying.Timers.Add(Board.Timers.Later(wait, () => Launch(flying)));
        var width = (int)Math.Ceiling(Math.Max(flight.From.W, item.W * 1.1) * State.Scale);
        _ = Decode(flying, FlightPicture.LoadAsync(flight.Jpeg, width));
    }

    public override void Removed(BoardItem item, Removal how) => Abort(item);

    public override void Tucking()
    {
        foreach (var item in _flying.Keys.ToArray())
        {
            Abort(item);
        }
        _tip.Drop();
    }

    async Task Decode(Flying flying, Task<BitmapImage?> decoding)
    {
        var picture = await decoding;
        if (picture is not null && !flying.Started && !flying.Aborted)
        {
            flying.Picture = picture;
            Launch(flying);
        }
    }

    /// <summary>The picture is ready, or the wait for it is over: the flight takes off.</summary>
    void Launch(Flying flying)
    {
        if (flying.Started || flying.Aborted)
        {
            return;
        }
        flying.Started = true;
        Stop(flying.Timers);
        var item = flying.Item;
        if (item.Leaving || !State.Revealed)
        {
            // Never took off: it pins on at the next reveal.
            _flying.Remove(item);
            return;
        }
        var flight = flying.Flight;
        var clock = new MotionClock();
        clock.SetScale(Board.Clock.Scale);
        clock.SetScale(flight.MotionScale);
        // Where the print rests once the row has scrolled to show it.
        var rest = new LogicalRect(State.WindowWidth / 2 + item.X - Board.Parts.Scroller.Target, item.Y, item.W, item.H);
        var lean = (item.Tilt < 0 ? -1 : 1) * (0.8 + Board.Random.NextDouble() * 0.8);
        var path = new FlightPath(flight.From, rest, item.Tilt, lean, flight.Found, State.WindowWidth, clock);
        var thumbnail = (item.View.Picture.Element as Image)?.Source;
        var stand = new FlightStandIn(Board, item, rest, flying.Picture ?? thumbnail);
        flying.StandIn = stand;
        Board.Layers.Overlay.Children.Add(stand.Root);

        var (keyframer, motions, view) = (Board.Keyframer, Board.Parts.PrintMotions, item.View);
        flying.Motions.Add(keyframer.Play(path.StandIn().Sample(), stand.Visual));
        flying.Motions.Add(keyframer.Play(path.Shade().Sample(), stand.Shade));
        flying.Motions.Add(keyframer.Play(path.Paper().Sample(), stand.Paper));
        flying.Motions.Add(keyframer.Play(path.Pin().Sample(), stand.Pin));
        // The print waits unseen and takes over as the stand-in touches the cork; its own pin drives in with the copy's.
        motions.Play(item, path.PrintHold(), view.Drop);
        motions.Play(item, path.Pin(), view.Pin);
        Board.Parts.Row.Unhide(item);

        var times = path.Times;
        flying.Timers.Add(Board.Timers.Later(times.PinAt, () => view.Arriving = true));
        flying.Timers.Add(Board.Timers.Later(times.TockAt, () => motions.Tock(item)));
        flying.Timers.Add(Board.Timers.Later(times.SettledAt, () =>
        {
            Land(flying);
            motions.Arrived(item);
            if (flight.Tip && !item.Leaving)
            {
                Board.Timers.Later(times.TipDelay, _tip.Show);
            }
        }));
    }

    /// <summary>The print has taken over: the stand-in goes.</summary>
    void Land(Flying flying)
    {
        _flying.Remove(flying.Item);
        Stop(flying.Timers);
        if (flying.StandIn is { } stand)
        {
            Board.Layers.Overlay.Children.Remove(stand.Root);
            stand.Release();
        }
        flying.StandIn = null;
        flying.Picture = null;
    }

    /// <summary>Ends <paramref name="item"/>'s flight at once, if it is flying.</summary>
    void Abort(BoardItem item)
    {
        if (!_flying.TryGetValue(item, out var flying))
        {
            return;
        }
        flying.Aborted = true;
        foreach (var motion in flying.Motions)
        {
            motion.Cancel();
        }
        Land(flying);
        item.View.Arriving = false;
    }

    void Stop(List<DispatcherQueueTimer> timers)
    {
        foreach (var timer in timers)
        {
            Board.Timers.Cancel(timer);
        }
        timers.Clear();
    }
}
