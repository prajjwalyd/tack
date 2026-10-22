using Tack.Core.Files;
using Tack.Core.Prints;
using Tack.Core.Screenshots;
using Tack.Core.Tests.Prints;

namespace Tack.Core.Tests.Screenshots;

public class FolderEventsTests
{
    static readonly string Pinned = BoardFixture.PathOf("pinned");
    static readonly string Fresh = BoardFixture.PathOf("fresh");

    static Board BoardWithOne()
    {
        var board = new Board();
        BoardFixture.Live(board, "pinned", Origin.Folder, 0);
        return board;
    }

    [Fact]
    public void A_new_image_is_decoded_once_however_many_events_it_brings()
    {
        var (board, events) = (BoardWithOne(), new FolderEvents());

        Assert.Equal(FileStep.Pin, events.Arrived(board, Fresh));
        Assert.Equal(FileStep.Ignore, events.Arrived(board, Fresh));
        Assert.Equal(FileStep.Ignore, events.Arrived(board, Fresh.ToUpperInvariant()));
        events.Finished(Fresh);
        Assert.Equal(FileStep.Pin, events.Arrived(board, Fresh));
    }

    [Fact]
    public void Files_that_are_not_screenshots_are_ignored()
    {
        var (board, events) = (BoardWithOne(), new FolderEvents());

        Assert.Equal(FileStep.Ignore, events.Arrived(board, @"C:\nowhere\notes.txt"));
        Assert.Equal(FileStep.Ignore, events.Arrived(board, @"C:\nowhere\anim.gif"));
    }

    [Fact]
    public void A_file_saved_over_a_pinned_one_is_redrawn()
    {
        var (board, events) = (BoardWithOne(), new FolderEvents());

        Assert.Equal(FileStep.Redraw, events.Arrived(board, Pinned));
    }

    [Fact]
    public void A_burst_of_writes_redraws_once_until_the_redraw_is_done()
    {
        var (board, events) = (BoardWithOne(), new FolderEvents());

        Assert.Equal(FileStep.Redraw, events.Changed(board, Pinned));
        Assert.Equal(FileStep.Ignore, events.Changed(board, Pinned));
        events.Finished(Pinned);
        Assert.Equal(FileStep.Redraw, events.Changed(board, Pinned));
    }

    [Fact]
    public void Writes_to_files_not_on_the_board_are_ignored()
    {
        var (board, events) = (BoardWithOne(), new FolderEvents());

        Assert.Equal(FileStep.Ignore, events.Changed(board, Fresh));
    }

    [Fact]
    public void Only_a_pinned_file_that_goes_is_checked_again_after_the_grace()
    {
        var (board, events) = (BoardWithOne(), new FolderEvents());

        Assert.Equal(FileStep.CheckGone, events.Gone(board, Pinned));
        Assert.Equal(FileStep.Ignore, events.Gone(board, Fresh));
    }

    [Fact]
    public void The_timings_are_unchanged()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), FolderEvents.WritePatience);
        Assert.Equal(TimeSpan.FromMilliseconds(500), FolderEvents.RemoveGrace);
        Assert.Equal(TimeSpan.FromMilliseconds(250), FolderEvents.UpdateSettle);
    }

    [Fact]
    public void A_print_whose_file_is_unchanged_is_not_redrawn()
    {
        var stamp = new FileStamp(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), 100);
        var picture = new Print { Id = "1", Path = Pinned, Stamp = stamp };
        var note = picture with { Kind = Kind.Note };

        Assert.Null(FolderEvents.WhatToRedraw(picture, stamp));
        Assert.Null(FolderEvents.WhatToRedraw(null, stamp));
        Assert.Equal(Kind.Image, FolderEvents.WhatToRedraw(picture, stamp with { Length = 101 }));
        Assert.Equal(Kind.Note, FolderEvents.WhatToRedraw(note, null));
        Assert.Equal(Kind.Image, FolderEvents.WhatToRedraw(picture with { Stamp = null }, null));
    }
}
