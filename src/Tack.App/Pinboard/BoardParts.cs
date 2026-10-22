using Tack.App.Pinboard.Automation;
using Tack.App.Pinboard.Chrome;
using Tack.App.Pinboard.Feedback;
using Tack.App.Pinboard.Input;
using Tack.App.Pinboard.Motion;
using Tack.App.Pinboard.Row;

namespace Tack.App.Pinboard;

/// <summary>
/// Every part of the board, made once. <see cref="All"/> is the order the
/// controller calls them in: the row lays out first, so the chrome, the
/// scroller and the motions see the new places; the feedback comes last.
/// </summary>
sealed class BoardParts
{
    public BoardParts(BoardContext board)
    {
        Row = new PrintRow(board);
        Chrome = new BoardChrome(board);
        Scroller = new Scroller(board);
        PrintMotions = new PrintMotions(board);
        Flights = new Flights(board);
        Gestures = new Gestures(board);
        Keyboard = new KeyboardInput(board);
        DropTarget = new DropTarget(board);
        NoticePill = new NoticePill(board);
        Announcer = new Announcer(board);
        Sounds = new Sounds(board);
        Captions = new Captions(board);
        Automation = new BoardAutomation(board);
        All = [Row, Chrome, Scroller, PrintMotions, Flights, Gestures, Keyboard, DropTarget, NoticePill, Announcer, Sounds, Captions, Automation];
    }

    public PrintRow Row { get; }

    public BoardChrome Chrome { get; }

    public Scroller Scroller { get; }

    public PrintMotions PrintMotions { get; }

    public Flights Flights { get; }

    public Gestures Gestures { get; }

    public KeyboardInput Keyboard { get; }

    public DropTarget DropTarget { get; }

    public NoticePill NoticePill { get; }

    public Announcer Announcer { get; }

    public Sounds Sounds { get; }

    public Captions Captions { get; }

    public BoardAutomation Automation { get; }

    public IReadOnlyList<BoardPart> All { get; }
}
