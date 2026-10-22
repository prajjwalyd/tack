using Microsoft.UI.Dispatching;
using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.Core.Diagnostics;
using Tack.Core.Pinboard;
using Tack.Core.Prints;
using Tack.Core.Reveal;

namespace Tack.App.Pinboard;

/// <summary>
/// The board's life: it hears the app's feed, keeps the board's state, swings
/// the hinge, and hands each change to every part in turn. The parts do the
/// rest, so a feature is a part's job, never this class's. UI thread only.
/// </summary>
sealed class BoardController
{
    readonly BoardContext _board;
    readonly DispatcherQueueTimer _cleanup;
    readonly DispatcherQueueTimer _resize;
    bool _synced;
    bool _parked;

    public BoardController(BoardLayers layers, nint window, IBoardActions actions, BoardFeed feed)
    {
        var queue = DispatcherQueue.GetForCurrentThread();
        _board = new BoardContext(layers, window, actions, queue);
        _cleanup = OneShot(queue, Cleanup);
        _resize = OneShot(queue, () =>
        {
            _board.Geometry.ForgetRect();
            Each(part => part.Resized());
        });
        feed.PrintAdded += (print, animate, flight) => Add(print, animate, flight, append: false);
        feed.PrintRemoved += Remove;
        feed.PrintUpdated += Update;
        feed.OrderChanged += ids => Each(part => part.Reordered(ids));
        feed.PrintCopied += id => Live(id, item => Each(part => part.Copied(item)));
        feed.PrintDragging += DragStarted;
        feed.PrintDragEnded += DragEnded;
        feed.Revealed += Reveal;
        feed.Tucking += Tuck;
        feed.PointerLeft += () => Each(part => part.PointerLeft());
        feed.Notice += text => Each(part => part.Notice(text));
        feed.LookChanged += ApplyLook;
        _board.Theme.Changed += ThemeChanged;
        _board.Motion.Changed += ReducedChanged;
        layers.Root.SizeChanged += (_, e) => Resize(e.NewSize.Width, e.NewSize.Height);
        layers.Root.Loaded += (_, _) =>
        {
            Rescale(layers.Root.XamlRoot.RasterizationScale);
            layers.Root.XamlRoot.Changed += (root, _) => Rescale(root.RasterizationScale);
        };
    }

    BoardState State => _board.State;

    /// <summary>
    /// The window is hidden and about to shrink to a pixel: the board stops
    /// following its size, and drops the perspective it only needs on screen.
    /// </summary>
    public void Parked()
    {
        _parked = true;
        _resize.Stop();
        _board.Hinge.Flatten();
    }

    /// <summary>The window is about to be shown: a board not down is folded, ready to swing.</summary>
    public void BeforeShow()
    {
        _parked = false;
        _board.Hinge.Unflatten();
        if (!State.Revealed)
        {
            _board.Hinge.Fold(State.Reduced);
        }
    }

    /// <summary>The first reveal: the prints the app already has, in row order, and the look.</summary>
    void Sync()
    {
        _synced = true;
        var snapshot = _board.Actions.Ready();
        _board.Look = snapshot.Look;
        _board.Clock.SetScale(snapshot.Look.MotionScale);
        foreach (var print in snapshot.Prints)
        {
            Add(print, animate: false, flight: null, append: true);
        }
        Each(part => part.LookChanged(snapshot.Look));
        Each(part => part.Synced());
    }

    void Reveal(RevealReason reason, double motionScale)
    {
        _cleanup.Stop();
        if (!_synced)
        {
            Sync();
        }
        _board.Clock.SetScale(motionScale);
        var was = State.Revealed;
        State.Revealed = true;
        if (!was)
        {
            _board.Hinge.Open(_board.Clock, State.Reduced);
        }
        _board.Geometry.ForgetRect();
        _board.Geometry.PushRect();
        Each(part => part.Revealed(reason, was));
    }

    void Tuck()
    {
        if (!State.Revealed)
        {
            return;
        }
        State.Revealed = false;
        Each(part => part.Tucking());
        _board.Timers.CancelAll();
        _board.Hinge.Close(_board.Clock, State.Reduced);
        _cleanup.Interval = TimeSpan.FromMilliseconds(_board.Clock.TuckCleanupMs);
        _cleanup.Start();
    }

    /// <summary>The fold is over: every motion stops and every part lets go, so nothing runs while tucked.</summary>
    void Cleanup()
    {
        if (State.Revealed)
        {
            return;
        }
        _board.Keyframer.CancelAll();
        Each(part => part.Tucked());
    }

    void Add(Print print, bool animate, Flight? flight, bool append)
    {
        if (State.Find(print.Id) is not null)
        {
            Update(print);
            return;
        }
        var item = _board.Parts.Row.Create(print);
        item.Flight = animate ? flight : null;
        // A new print goes at the end of the kept ones if kept, else first among the rest.
        var at = append ? State.Order.Count : State.KeptCount;
        State.Items[print.Id] = item;
        State.Order.Insert(at, print.Id);
        _board.Layers.Track.Children.Add(item.View.Slot.Element);
        Each(part => part.Added(item, animate, flight));
    }

    void Remove(string id, Removal how)
    {
        if (State.Find(id) is not { Leaving: false } item)
        {
            return;
        }
        item.Leaving = true;
        item.IndexWhenRemoved = State.Order.IndexOf(id);
        State.Order.Remove(id);
        State.Awaiting.Remove(item);
        if (State.Hovered == item)
        {
            State.Hovered = null;
        }
        Each(part => part.Removed(item, how));
    }

    void Update(Print print) => Live(print.Id, item =>
    {
        var old = item.Print;
        item.Print = print;
        item.View.Update(print);
        Each(part => part.Updated(item, old));
    });

    void DragStarted(string id)
    {
        State.DraggingId = id;
        Each(part => part.DragStarted(id, State.Find(id)));
    }

    void DragEnded(string id)
    {
        if (State.DraggingId == id)
        {
            State.DraggingId = null;
        }
        Each(part => part.DragEnded(id, State.Find(id)));
    }

    void ApplyLook(BoardLook look)
    {
        _board.Look = look;
        _board.Clock.SetScale(look.MotionScale);
        // The note paper is drawn by the prints themselves.
        foreach (var item in State.Items.Values)
        {
            item.View.ApplyTheme();
        }
        Each(part => part.LookChanged(look));
    }

    void ThemeChanged()
    {
        foreach (var item in State.Items.Values)
        {
            item.View.ApplyTheme();
        }
        Each(part => part.ThemeChanged());
    }

    void ReducedChanged()
    {
        State.Reduced = _board.Motion.Reduced;
        if (!State.Revealed)
        {
            _board.Hinge.Fold(State.Reduced);
        }
        Each(part => part.ReducedChanged());
    }

    void Resize(double width, double height)
    {
        // Parked, or back at the size it had: nothing to lay out again.
        if (_parked || (width, height) == (State.WindowWidth, State.WindowHeight))
        {
            return;
        }
        (State.WindowWidth, State.WindowHeight) = (width, height);
        _resize.Stop();
        _resize.Interval = TimeSpan.FromMilliseconds(BoardMetrics.ResizeDebounceMs);
        _resize.Start();
    }

    void Rescale(double scale)
    {
        if (scale > 0 && scale != State.Scale)
        {
            State.Scale = scale;
            Resize(State.WindowWidth, State.WindowHeight);
        }
    }

    /// <summary>Runs <paramref name="then"/> for a print still on the board and not leaving.</summary>
    void Live(string id, Action<BoardItem> then)
    {
        if (State.Find(id) is { Leaving: false } item)
        {
            then(item);
        }
    }

    /// <summary>Calls every part in order. One that throws is traced, and the rest still run.</summary>
    void Each(Action<BoardPart> call)
    {
        foreach (var part in _board.Parts.All)
        {
            try
            {
                call(part);
            }
            catch (Exception e)
            {
                DevTrace.Write($"board: {part.GetType().Name} failed: {e}");
            }
        }
    }

    static DispatcherQueueTimer OneShot(DispatcherQueue queue, Action run)
    {
        var timer = queue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            run();
        };
        return timer;
    }
}
