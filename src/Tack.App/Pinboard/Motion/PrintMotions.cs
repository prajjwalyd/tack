using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.App.Pinboard.Prints;
using Tack.Core.Pinboard.Motion;
using Tack.Core.Prints;
using Tack.Core.Reveal;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// How prints move: the pin-on as they arrive (the row's arrivals say when),
/// the keep press as the app's keep comes back (the row's new order waits for
/// it), the glint across a kept pin as the board comes down, the fall and the
/// quiet removal, the copied check, the refreshed picture's flash, the swing
/// and the breeze. Under reduced motion they fade instead. Everything stops
/// on a tuck and every layer is put back at rest, so nothing runs while tucked.
/// </summary>
sealed class PrintMotions : BoardPart
{
    readonly Leaving _leaving;
    readonly CopiedBadges _badges;
    readonly Gusts _gusts;

    /// <summary>Prints whose layers a motion has moved since the last tuck.</summary>
    readonly HashSet<BoardItem> _touched = [];

    public PrintMotions(BoardContext board) : base(board)
    {
        Swings = new Swings(board);
        _leaving = new Leaving(board);
        _badges = new CopiedBadges(board);
        _gusts = new Gusts(board, Swings);
    }

    /// <summary>The prints' swings on their pins.</summary>
    public Swings Swings { get; }

    BoardState State => Board.State;

    MotionClock Clock => Board.Clock;

    /// <summary>
    /// The arrival, the print already showing: held just above the cork,
    /// pressed down, then the pin drives in with a tock and the print swings
    /// against its tilt. Under reduced motion it fades in and the pin follows.
    /// </summary>
    public void PinOn(BoardItem item)
    {
        var view = item.View;
        Touch(item);
        if (State.Reduced)
        {
            Play(BoardMotions.ReducedArrival(Clock), view.Drop);
            Play(BoardMotions.ReducedPin(Clock), view.Pin);
            Board.Timers.Later(Clock.Scaled(BoardMotions.ReducedTockMs), () => Tock(item));
            return;
        }
        view.Arriving = true;
        var side = item.Tilt < 0 ? -1 : 1;
        var lean = side * (0.8 + Board.Random.NextDouble() * 0.8);
        var pinAt = Clock.Scaled(BoardMotions.PinAtMs);
        Play(BoardMotions.PinOnDrop(Clock, lean), view.Drop);
        Play(BoardMotions.PinOnShade(Clock), view.Shade);
        Play(BoardMotions.PinDrop(Clock, pinAt), view.Pin);
        Play(BoardMotions.Dimple(Clock, PrintView.PinShadowOpacity), view.PinShadow);
        Board.Timers.Later(pinAt + Clock.Scaled(BoardMotions.TockAfterMs), () => Tock(item));
        Board.Timers.Later(pinAt + Clock.Scaled(BoardMotions.ArrivedAfterMs), () => Arrived(item));
        Glint(item);
    }

    /// <summary>The print is on the cork: it leaves the arriving place and swings, against its tilt.</summary>
    public void Arrived(BoardItem item)
    {
        item.View.Arriving = false;
        Swings.Nudge(item, -(item.Tilt < 0 ? -1 : 1) * BoardMotions.ArrivalSwing);
    }

    /// <summary>The pin goes into the cork, if the print is still pinned.</summary>
    public void Tock(BoardItem item)
    {
        if (!item.Leaving)
        {
            Board.Parts.Sounds.Tock();
        }
    }

    /// <summary>Plays <paramref name="timeline"/> on one of <paramref name="item"/>'s layers; a tuck puts the layer back at rest.</summary>
    public Playing Play(BoardItem item, Timeline timeline, PrintLayer layer, Action? done = null)
    {
        Touch(item);
        return Play(timeline, layer, done);
    }

    public override void Revealed(RevealReason reason, bool wasRevealed)
    {
        if (!wasRevealed)
        {
            // The board swings into the light: every kept pin glints.
            foreach (var item in State.Row.Where(item => !Board.Parts.Row.IsHidden(item)))
            {
                Glint(item);
            }
        }
        _gusts.Start();
    }

    public override void Tucking() => _gusts.Stop();

    public override void Tucked()
    {
        _gusts.Stop();
        Swings.Clear();
        _badges.Clear();
        _leaving.ForgetAll();
        foreach (var item in _touched.Where(item => !item.Leaving))
        {
            MotionRest.Put(item, Board.Keyframer);
        }
        _touched.Clear();
    }

    public override void Added(BoardItem item, bool animate, Flight? flight)
    {
        if (!animate && !Board.Parts.Row.IsHidden(item))
        {
            Glint(item);
        }
    }

    /// <summary>A leaving print is taken off the board once its fall or fade is over.</summary>
    public override void Removed(BoardItem item, Removal how)
    {
        Swings.Stop(item);
        _badges.Drop(item);
        _touched.Remove(item);
        _leaving.Leave(item, how);
    }

    public override void Updated(BoardItem item, Print old)
    {
        if (item.Print.Kept != old.Kept && State.Revealed)
        {
            KeepPress(item, old.Kept);
        }
        if (!State.Revealed || State.Reduced)
        {
            return;
        }
        if (item.Print.Note is { } note && !note.Equals(old.Note))
        {
            Play(item, BoardMotions.PaperFlash(Clock), item.View.Paper);
        }
        else if (item.Print.Thumb is { } thumb && !ReferenceEquals(thumb.Jpeg, old.Thumb?.Jpeg) && old.Thumb is not null)
        {
            Play(item, BoardMotions.PictureFlash(Clock), item.View.Picture);
        }
    }

    public override void Copied(BoardItem item)
    {
        Touch(item);
        _badges.Stamp(item);
        Swings.Nudge(item, (item.Tilt < 0 ? -1 : 1) * BoardMotions.CopySwing);
    }

    public override void ReducedChanged()
    {
        if (State.Reduced)
        {
            _gusts.Stop();
            Swings.Clear();
            return;
        }
        _gusts.Start();
    }

    /// <summary>
    /// The app kept the print, or stopped: the pin is pressed in and springs
    /// back, the look flipping at the bottom of the press, and the row's new
    /// order waits for the press. Under reduced motion the look just fades over.
    /// </summary>
    void KeepPress(BoardItem item, bool was)
    {
        var view = item.View;
        // The view already shows the new state: it flips at the bottom of the press instead.
        view.SetKept(was, animate: false);
        if (State.Reduced)
        {
            view.SetKept(item.Print.Kept, animate: true);
            return;
        }
        var press = Clock.Scaled(BoardMotions.KeepPressMs);
        Play(item, BoardMotions.KeepPin(Clock), view.Pin);
        Play(item, BoardMotions.KeepDrop(Clock), view.Drop);
        Board.Parts.Row.HoldOrder(press);
        Board.Timers.Later(press * BoardMotions.KeepFlip, () =>
        {
            if (!item.Leaving)
            {
                view.SetKept(item.Print.Kept, animate: true);
                Glint(item);
            }
        });
    }

    /// <summary>A band of light crosses a kept pin's brass, while the board is down and full motion is wanted.</summary>
    void Glint(BoardItem item)
    {
        if (item.Print.Kept && State.Revealed && !State.Reduced && !item.Leaving)
        {
            Play(item, BoardMotions.Glint(Clock, PrintView.GlintTravel), item.View.Glint);
        }
    }

    void Touch(BoardItem item) => _touched.Add(item);

    Playing Play(Timeline timeline, PrintLayer layer, Action? done = null) =>
        Board.Keyframer.Play(timeline.Sample(), layer.Visual, done);
}
