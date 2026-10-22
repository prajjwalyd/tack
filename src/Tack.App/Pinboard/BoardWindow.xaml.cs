using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Tack.App.Pinboard.Contract;
using Tack.Windows.Edge;
using Tack.Windows.Windowing;

namespace Tack.App.Pinboard;

/// <summary>
/// The board's window: see-through, topmost and never activated, as large as
/// the work area of the monitor it comes down on, with the board at its top.
/// Created hidden at startup, so the first reveal draws at once; the
/// <see cref="BoardController"/> runs the board in it. UI thread only.
/// </summary>
public sealed partial class BoardWindow : Window
{
    /// <summary>How long after hiding the window is parked, once its last frames have gone through.</summary>
    static readonly TimeSpan ParkAfter = TimeSpan.FromSeconds(1);

    BoardController? _controller;
    bool _visible;
    DispatcherQueueTimer? _park;

    public BoardWindow()
    {
        InitializeComponent();
        SystemBackdrop = new TransparentBackdrop();
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Overlay.MakeUnfocusable(Handle);
        SeeThrough.Apply(Handle);
    }

    public nint Handle { get; }

    /// <summary>Starts the board: it asks the app through <paramref name="actions"/> and hears it through <paramref name="feed"/>.</summary>
    internal void Start(IBoardActions actions, BoardFeed feed) =>
        _controller = new BoardController(
            new BoardLayers(Root, Stage, Board, Cork, Rail, Track, Veil, OverlayLayer), Handle, actions, feed);

    /// <summary>Shows the window at <paramref name="place"/>, click-through and not activated; a board not down is folded first.</summary>
    internal void ShowAt(Placement place)
    {
        if (!_visible)
        {
            _park?.Stop();
            _controller?.BeforeShow();
        }
        Overlay.ShowAt(Handle, place.Position, place.Size);
        _visible = true;
    }

    /// <summary>Moves the shown window to <paramref name="place"/>, the board as it is.</summary>
    internal void MoveTo(Placement place) => Overlay.ShowAt(Handle, place.Position, place.Size);

    /// <summary>
    /// Hides the window and parks it: a pixel wide and flat, so it holds next to
    /// nothing in drawing buffers until the next reveal.
    /// </summary>
    public void Hide()
    {
        Overlay.Hide(Handle);
        _visible = false;
        _controller?.Parked();
        _park ??= ParkTimer();
        _park.Start();
    }

    DispatcherQueueTimer ParkTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = ParkAfter;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (!_visible)
            {
                // Through the window's own sizing, which its drawing surface follows even hidden.
                AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1, 1));
            }
        };
        return timer;
    }
}
