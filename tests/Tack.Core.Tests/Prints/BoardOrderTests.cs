using Tack.Core.Prints;
using static Tack.Core.Tests.Prints.BoardFixture;

namespace Tack.Core.Tests.Prints;

public class BoardOrderTests
{
    [Fact]
    public void New_prints_join_at_the_front_newest_first()
    {
        var board = new Board();
        PinMany(board, 0, 3);

        Assert.Equal(["3", "2", "1"], Ids(board));
        Assert.Equal(["3", "2", "1"], board.Order());
        Assert.Equal(Now + 2 * Min, board.Find("3")!.PinnedAt);
        Assert.False(board.Find("3")!.Kept);
    }

    [Fact]
    public void Kept_prints_lead_in_the_order_they_were_kept()
    {
        var board = new Board();
        PinMany(board, 0, 4);

        var first = board.SetKept("2", true, Now + 10 * Min)!;
        Assert.True(first.Moved);
        Assert.True(first.Print.Kept);
        Assert.Equal(Now + 10 * Min, first.Print.KeptAt);
        board.SetKept("4", true, Now + 11 * Min);
        Assert.Equal(["2", "4", "3", "1"], Ids(board));
        // A newcomer joins after the kept prints, at the front of the rest.
        PinMany(board, 20, 1);
        Assert.Equal(["2", "4", "5", "3", "1"], Ids(board));
    }

    [Fact]
    public void Keeping_twice_changes_nothing()
    {
        var board = new Board();
        PinMany(board, 0, 3);
        board.SetKept("1", true, Now + 10 * Min);
        board.SetKept("2", true, Now + 11 * Min);

        var again = board.SetKept("1", true, Now + 12 * Min)!;

        Assert.False(again.Moved);
        Assert.Equal(Now + 10 * Min, again.Print.KeptAt);
        Assert.Equal(["1", "2", "3"], Ids(board));
        Assert.Null(board.SetKept("nope", true, Now));
    }

    [Fact]
    public void A_print_no_longer_kept_goes_back_by_when_it_was_pinned()
    {
        var board = new Board();
        PinMany(board, 0, 4);
        board.SetKept("2", true, Now + 10 * Min);
        Assert.Equal(["2", "4", "3", "1"], Ids(board));

        var change = board.SetKept("2", false, Now + 11 * Min)!;

        Assert.True(change.Moved);
        Assert.False(change.Print.Kept);
        Assert.Null(change.Print.KeptAt);
        Assert.Empty(change.Aged);
        Assert.Equal(["4", "3", "2", "1"], Ids(board));
    }

    [Fact]
    public void A_print_no_longer_kept_ages_out_if_it_is_past_the_history()
    {
        var board = new Board();
        PinMany(board, 0, 2);
        board.SetKept("1", true, Now + 5 * Min);
        // Eight days later the unkept one has aged out; the kept one stays.
        var later = Now + History.MaxAgeMs + 24 * 60 * Min;
        Assert.Equal(["2"], Ids(board.AgeOut(later)));
        Assert.Equal(["1"], Ids(board));

        var change = board.SetKept("1", false, later)!;

        Assert.Equal(["1"], Ids(change.Aged));
        Assert.Empty(board.Prints);
    }

    [Fact]
    public void The_fifty_first_unkept_print_ages_out_the_oldest()
    {
        var board = new Board();
        PinMany(board, 0, History.MaxUnkept);
        // Kept prints do not count against the history.
        board.SetKept("1", true, Now + 100 * Min);
        board.SetKept("2", true, Now + 101 * Min);
        PinMany(board, 200, 2);
        Assert.Equal(History.MaxUnkept + 2, board.Prints.Count);

        var outcome = Assert.IsType<PinOutcome.New>(Live(board, "fresh", Origin.Folder, 300));

        Assert.Equal("53", outcome.Print.Id);
        // The oldest unkept print is 3 (1 and 2 are kept).
        Assert.Equal(["3"], Ids(outcome.Aged));
        Assert.Equal(["1", "2", "53"], Ids(board)[..3]);
        Assert.Equal(History.MaxUnkept, board.Prints.Count(p => !p.Kept));
    }

    [Fact]
    public void Unkept_prints_age_out_after_a_week_and_kept_ones_never()
    {
        var board = new Board();
        PinMany(board, 0, 3);
        board.SetKept("1", true, Now + 10 * Min);
        // A week after print 2 was pinned: 2 goes, 3 (a minute younger) stays.
        var week = Now + Min + History.MaxAgeMs;

        Assert.Equal(["2"], Ids(board.AgeOut(week)));
        Assert.Equal(["1", "3"], Ids(board));
        Assert.Empty(board.AgeOut(week));
        var years = Now + 400 * History.MaxAgeMs;
        Assert.Equal(["3"], Ids(board.AgeOut(years)));
        Assert.Equal(["1"], Ids(board));
    }

    [Fact]
    public void Restored_prints_come_back_in_place()
    {
        var board = new Board();
        // Saved in row order: a kept print, then the history newest first.
        Restored(board, "kept", Now - 30 * History.MaxAgeMs, Now - History.MaxAgeMs);
        Restored(board, "newer", Now - 2 * Min, null);
        Restored(board, "older", Now - 5 * Min, null);

        Assert.Equal(["1", "2", "3"], Ids(board));
        var kept = board.Find("1")!;
        Assert.True(kept.Kept);
        Assert.Equal(Now - 30 * History.MaxAgeMs, kept.PinnedAt);
        Assert.Null(kept.Arrived);
    }

    [Fact]
    public void A_restored_print_past_the_history_is_not_pinned()
    {
        var board = new Board();

        var outcome = Assert.IsType<PinOutcome.Expired>(Restored(board, "old", Now - History.MaxAgeMs - Min, null));

        Assert.Equal(PathOf("old"), outcome.Print.Path);
        Assert.Empty(outcome.Aged);
        Assert.Empty(board.Prints);
    }

    [Fact]
    public void Repointing_keeps_the_print_and_returns_the_old_path()
    {
        var board = new Board();
        Live(board, "capture", Origin.Capture, 0);

        var (print, old) = board.Repoint("1", PathOf("saved"), Origin.Folder)!.Value;

        Assert.Equal(PathOf("capture"), old);
        Assert.Equal("saved.png", print.Name);
        Assert.Equal(Origin.Folder, board.Find("1")!.Origin);
        Assert.Null(board.Repoint("nope", PathOf("x"), Origin.Folder));
    }
}
