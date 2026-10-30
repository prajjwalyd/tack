using Tack.Core.Pinboard.Input;
using Tack.Core.Prints;
using Tack.Core.Tests.Prints;

namespace Tack.Core.Tests.Pinboard.Input;

/// <summary>A linked file's menu: the user's own file, so nothing that edits or recycles it.</summary>
public class PrintMenuFileTests
{
    static string[] Labels(IReadOnlyList<PrintMenuEntry> entries) => [.. entries.Select(e => e.IsSeparator ? "-" : e.Label)];

    [Fact]
    public void A_file_can_be_copied_opened_shown_kept_and_unpinned_only() =>
        Assert.Equal(["Copy", "Open", "Show in Explorer", "-", "Keep", "Unpin"], Labels(PrintMenuModel.ForFile(kept: false)));

    [Fact]
    public void A_kept_file_offers_to_stop_keeping_it() =>
        Assert.Equal(["Copy", "Open", "Show in Explorer", "-", "Stop keeping", "Unpin"], Labels(PrintMenuModel.ForFile(kept: true)));

    [Fact]
    public void A_file_is_never_edited_saved_to_pictures_or_recycled()
    {
        foreach (var kept in new[] { false, true })
        {
            var actions = PrintMenuModel.ForFile(kept).Select(e => e.Action).ToList();
            Assert.DoesNotContain(PrintMenuAction.Edit, actions);
            Assert.DoesNotContain(PrintMenuAction.SaveToPictures, actions);
            Assert.DoesNotContain(PrintMenuAction.Recycle, actions);
        }
    }

    [Fact]
    public void The_menu_follows_a_linked_file_even_one_claiming_to_be_a_capture()
    {
        var board = new Board();
        board.PinFile(@"C:\Users\me\deck.pptx", null, new FileFacts(false, 1, ""), new Arrival.Live(BoardFixture.Start), BoardFixture.Now);
        var print = board.Prints[0] with { Origin = Origin.Capture };

        Assert.Equal(Labels(PrintMenuModel.ForFile(kept: false)), Labels(PrintMenuModel.For(print)));
    }
}
