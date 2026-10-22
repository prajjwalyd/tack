using Tack.Core.Prints;
using static Tack.Core.Tests.Prints.BoardFixture;

namespace Tack.Core.Tests.Prints;

/// <summary>The two halves of an auto-saved screenshot: Snipping Tool both saves a file and copies the image.</summary>
public class BoardTwinTests
{
    [Fact]
    public void A_screenshot_file_after_its_capture_replaces_it()
    {
        var board = new Board();
        Assert.IsType<PinOutcome.New>(Live(board, "capture", Origin.Capture, 0));
        var soon = new Arrival.Live(Start + TimeSpan.FromSeconds(1));

        var merged = Assert.IsType<PinOutcome.Merged>(board.Pin(PathOf("file"), Thumb(1920, 1080), Origin.Folder, soon, Now + 1000));

        Assert.Equal(PathOf("capture"), merged.Capture);
        Assert.Equal("1", merged.Print.Id);
        Assert.Equal(PathOf("file"), merged.Print.Path);
        Assert.Equal("file.png", merged.Print.Name);
        Assert.Equal(Origin.Folder, merged.Print.Origin);
        Assert.Equal(Now, merged.Print.PinnedAt);
        Assert.Single(board.Prints);
    }

    [Fact]
    public void A_capture_after_its_screenshot_file_is_a_duplicate()
    {
        var board = new Board();
        board.Pin(PathOf("file"), Thumb(1920, 1080), Origin.Folder, new Arrival.Live(Start), Now);
        var later = new Arrival.Live(Start + TimeSpan.FromSeconds(1));

        var outcome = board.Pin(PathOf("capture"), Thumb(1920, 1080), Origin.Capture, later, Now + 1000);

        Assert.IsType<PinOutcome.Duplicate>(outcome);
        Assert.Equal(["1"], Ids(board));
        Assert.Equal(Origin.Folder, board.Prints[0].Origin);
    }

    [Fact]
    public void Twins_must_be_recent_and_the_same_size()
    {
        var board = new Board();
        board.Pin(PathOf("capture"), Thumb(1920, 1080), Origin.Capture, new Arrival.Live(Start), Now);
        // Too late to be the same screenshot.
        var late = new Arrival.Live(Start + Board.TwinWindow);
        Assert.IsType<PinOutcome.New>(board.Pin(PathOf("file"), Thumb(1920, 1080), Origin.Folder, late, Now));
        // Another size is another screenshot.
        Assert.IsType<PinOutcome.New>(board.Pin(PathOf("small"), Thumb(640, 480), Origin.Folder, new Arrival.Live(Start), Now));
        // Restored prints never merge.
        var again = new Arrival.Restored(Now, null);
        Assert.IsType<PinOutcome.New>(board.Pin(PathOf("restored"), Thumb(1920, 1080), Origin.Folder, again, Now));
    }

    [Fact]
    public void The_same_size_is_not_enough_to_be_twins()
    {
        var board = new Board();
        board.Pin(PathOf("capture"), WithHash(1920, 1080, 1), Origin.Capture, new Arrival.Live(Start), Now);
        // Another picture of the same size moments later: both stay.
        var soon = new Arrival.Live(Start + TimeSpan.FromSeconds(1));
        Assert.IsType<PinOutcome.New>(board.Pin(PathOf("file"), WithHash(1920, 1080, 2), Origin.Folder, soon, Now));
        // Pixels nobody hashed cannot be shown to be the same, either way.
        Assert.IsType<PinOutcome.New>(board.Pin(PathOf("unknown"), WithHash(1920, 1080, null), Origin.Folder, soon, Now));
        var later = new Board();
        later.Pin(PathOf("file"), WithHash(800, 600, null), Origin.Folder, new Arrival.Live(Start), Now);
        Assert.IsType<PinOutcome.New>(later.Pin(PathOf("capture"), WithHash(800, 600, null), Origin.Capture, soon, Now));

        Assert.Equal(3, board.Prints.Count);
        Assert.Equal(2, later.Prints.Count);
    }

    [Fact]
    public void The_same_file_is_pinned_once()
    {
        var board = new Board();
        Live(board, "a", Origin.Folder, 0);

        var outcome = board.Pin(@"C:\NOWHERE\A.PNG", Thumb(1, 1), Origin.Folder, new Arrival.Live(Start), Now);

        Assert.IsType<PinOutcome.Exists>(outcome);
    }
}
