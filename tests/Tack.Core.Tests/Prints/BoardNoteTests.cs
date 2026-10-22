using Tack.Core.Prints;
using static Tack.Core.Tests.Prints.BoardFixture;

namespace Tack.Core.Tests.Prints;

public class BoardNoteTests
{
    [Fact]
    public void Notes_join_the_same_row_as_prints()
    {
        var board = new Board();
        Live(board, "shot", Origin.Folder, 0);

        var outcome = Assert.IsType<PinOutcome.New>(LiveNote(board, "note", "Call Sam back", 1));

        Assert.Empty(outcome.Aged);
        var print = outcome.Print;
        Assert.Equal(Kind.Note, print.Kind);
        Assert.True(print.Owned, "Tack wrote the note's file, so it cleans it up");
        Assert.Equal("Call Sam back", print.Note!.Text);
        Assert.Equal((null, 0, 0), (print.Thumb, print.Width, print.Height));
        Assert.Equal("note.txt", print.Name);
        Assert.Equal(["2", "1"], Ids(board));
        // Kept like any print.
        board.SetKept("2", true, Now + 5 * Min);
        Live(board, "later", Origin.Folder, 6);
        Assert.Equal(["2", "3", "1"], Ids(board));
    }

    [Fact]
    public void The_same_text_is_pinned_once()
    {
        var board = new Board();
        LiveNote(board, "a", "https://example.org", 0);

        Assert.IsType<PinOutcome.Exists>(LiveNote(board, "b", "https://example.org", 1));
        Assert.IsType<PinOutcome.New>(LiveNote(board, "c", "https://example.org/other", 2));
        // A restored note never counts as a repeat: board.json said it was there.
        var restored = new Arrival.Restored(Now, null);
        Assert.IsType<PinOutcome.New>(board.PinNote(NotePath("d"), Note("https://example.org"), restored, Now));
        Assert.Equal("1", board.FindNote("https://example.org")!.Id);
    }

    [Fact]
    public void Notes_age_out_like_prints()
    {
        var board = new Board();
        LiveNote(board, "old", "old news", 0);
        LiveNote(board, "kept", "keep me", 1);
        board.SetKept("2", true, Now + 2 * Min);

        Assert.Equal(["1"], Ids(board.AgeOut(Now + History.MaxAgeMs + Min)));
        Assert.Equal(["2"], Ids(board));
        // They count toward the history's fifty, too.
        PinMany(board, 10, History.MaxUnkept);
        var outcome = Assert.IsType<PinOutcome.New>(LiveNote(board, "new", "one more", 100));
        Assert.Single(outcome.Aged);
        Assert.Equal(History.MaxUnkept, board.Prints.Count(p => !p.Kept));
    }

    [Fact]
    public void A_note_never_merges_with_a_screenshot()
    {
        var board = new Board();
        board.PinNote(NotePath("n"), Note("text"), new Arrival.Live(Start), Now);

        // A screenshot file "of the same size" (0 x 0) right after it.
        var outcome = board.Pin(PathOf("file"), Thumb(0, 0), Origin.Folder, new Arrival.Live(Start), Now);

        Assert.IsType<PinOutcome.New>(outcome);
        Assert.Equal(2, board.Prints.Count);
    }

    [Fact]
    public void An_edited_note_file_updates_its_text()
    {
        var board = new Board();
        LiveNote(board, "n", "draft", 0);

        var print = board.UpdateNote(NotePath("n"), Note("final"))!;

        Assert.Equal("final", print.Note!.Text);
        Assert.Null(board.UpdateNote(PathOf("n"), Note("x")));
    }
}
