using Tack.Core.Prints;
using Tack.Core.Thumbnails;
using static Tack.Core.Tests.Prints.BoardFixture;

namespace Tack.Core.Tests.Prints;

/// <summary>Linked files on the board model: pinned where they are, the user's own, once each.</summary>
public class BoardFileTests
{
    const string Report = @"C:\Users\me\Report.docx";
    static readonly FileFacts Doc = new(false, 2048, "Microsoft Word Document");

    static PinOutcome Pin(Board board, string path, long n, Thumb? thumb = null) =>
        board.PinFile(path, thumb, Doc, new Arrival.Live(Start + TimeSpan.FromMinutes(n)), Now + n * Min);

    [Fact]
    public void A_file_is_pinned_where_it_is_and_never_as_tacks_own()
    {
        var board = new Board();

        var added = Assert.IsType<PinOutcome.New>(Pin(board, Report, 0, WithHash(240, 320, null)));

        var print = added.Print;
        Assert.Equal((Kind.File, Origin.Folder, false), (print.Kind, print.Origin, print.Owned));
        Assert.Equal((Report, "Report.docx", Doc), (print.Path, print.Name, print.File));
        Assert.True(print.IsFile);
        Assert.False(print.IsNote);
        Assert.Equal((240, 320), (print.Width, print.Height));
    }

    [Fact]
    public void A_file_already_on_the_board_is_not_pinned_twice_whatever_its_case()
    {
        var board = new Board();
        Pin(board, Report, 0);

        Assert.IsType<PinOutcome.Exists>(Pin(board, Report.ToUpperInvariant(), 1));
        Assert.Single(board.Prints);
    }

    [Fact]
    public void A_file_with_no_thumbnail_is_pinned_all_the_same()
    {
        var board = new Board();

        var added = Assert.IsType<PinOutcome.New>(Pin(board, @"C:\Users\me\odd.bin", 0));

        Assert.Null(added.Print.Thumb);
        Assert.Equal((0, 0), (added.Print.Width, added.Print.Height));
    }

    [Fact]
    public void Files_and_screenshots_share_one_row_newest_first()
    {
        var board = new Board();
        Live(board, "shot", Origin.Folder, 0);
        Pin(board, Report, 1);
        LiveNote(board, "n", "hello", 2);

        Assert.Equal([Kind.Note, Kind.File, Kind.Image], board.Prints.Select(p => p.Kind));
    }

    [Fact]
    public void A_restored_file_keeps_its_times()
    {
        var board = new Board();

        board.PinFile(Report, null, Doc, new Arrival.Restored(Now - 5 * Min, Now - Min), Now);

        Assert.Equal((Now - 5 * Min, Now - Min), (board.Prints[0].PinnedAt, board.Prints[0].KeptAt));
    }
}
