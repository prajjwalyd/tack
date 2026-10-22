using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Hosting;
using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.App.Pinboard.Motion;
using Tack.App.Pinboard.Prints;
using Tack.Core.Pinboard.Motion;
using Tack.Core.Settings;

namespace Tack.App.Pinboard;

/// <summary>
/// Everything the board's parts share: the state, the app's actions, the
/// theme, the clock, the timers, the keyframer, the window's layers, and the
/// other parts (through <see cref="Parts"/>, so one part can ask another).
/// Made once by the controller. UI thread only.
/// </summary>
sealed class BoardContext
{
    public BoardContext(BoardLayers layers, nint window, IBoardActions actions, DispatcherQueue queue)
    {
        (Layers, Window, Actions, Queue) = (layers, window, actions, queue);
        Theme = new BoardTheme(layers.Root, queue);
        Motion = new MotionPreference(queue);
        State = new BoardState { Reduced = Motion.Reduced };
        Timers = new BoardTimers(queue);
        Keyframer = new Keyframer(ElementCompositionPreview.GetElementVisual(layers.Root).Compositor);
        Hinge = new Hinge(layers, Keyframer);
        Geometry = new BoardGeometry(this);
        // Last: the parts are given this context, complete but for the parts themselves.
        Parts = new BoardParts(this);
    }

    public BoardLayers Layers { get; }

    /// <summary>The board window's handle.</summary>
    public nint Window { get; }

    public IBoardActions Actions { get; }

    /// <summary>The UI thread's queue.</summary>
    public DispatcherQueue Queue { get; }

    public BoardState State { get; }

    public BoardTheme Theme { get; }

    /// <summary>Whether Windows asks for less motion; <see cref="BoardState.Reduced"/> follows it.</summary>
    public MotionPreference Motion { get; }

    /// <summary>The Speed setting's scale, for every duration on the board.</summary>
    public MotionClock Clock { get; } = new();

    /// <summary>Choreography timers, all cancelled on tuck.</summary>
    public BoardTimers Timers { get; }

    public Keyframer Keyframer { get; }

    /// <summary>The board's swing down and fold up.</summary>
    public Hinge Hinge { get; }

    /// <summary>The board's width and where the window takes the pointer.</summary>
    public BoardGeometry Geometry { get; }

    /// <summary>The other parts. Not yet set while the parts are being made: never read it in a part's constructor.</summary>
    public BoardParts Parts { get; }

    /// <summary>The settings the board follows, as last heard.</summary>
    public BoardLook Look { get; set; } = BoardLook.Of(new TackSettings());

    /// <summary>For tilts, leans and the breeze.</summary>
    public Random Random { get; } = Random.Shared;

    /// <summary>A new print's layers, in the current theme.</summary>
    public PrintView NewView() => new(Theme, this);

    /// <summary>A leaving print is gone: off the track and out of the board's state.</summary>
    public void Forget(BoardItem item)
    {
        Layers.Track.Children.Remove(item.View.Slot.Element);
        if (State.Find(item.Id) == item)
        {
            State.Items.Remove(item.Id);
            State.Order.Remove(item.Id);
        }
        State.Awaiting.Remove(item);
        if (State.Hovered == item)
        {
            State.Hovered = null;
        }
    }
}
