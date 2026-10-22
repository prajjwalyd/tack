using Tack.Core.Notes;
using Tack.Core.Pinboard.Input;
using Tack.Core.Prints;
using Tack.Core.Tests.Prints;

namespace Tack.Core.Tests.Pinboard.Input;

public class PrintMenuModelTests
{
    static string[] Labels(IReadOnlyList<PrintMenuEntry> entries) => [.. entries.Select(e => e.IsSeparator ? "-" : e.Label)];

    [Fact]
    public void A_screenshot_file_has_every_line_but_Save_to_Pictures() =>
        Assert.Equal(
            ["Copy", "Open", "Edit", "Show in Explorer", "-", "Keep", "Unpin", "Move to Recycle Bin"],
            Labels(PrintMenuModel.For(note: false, link: false, capture: false, kept: false)));

    [Fact]
    public void A_capture_can_be_saved_to_Pictures_after_Show_in_Explorer() =>
        Assert.Equal(
            ["Copy", "Open", "Edit", "Show in Explorer", "Save to Pictures", "-", "Keep", "Unpin", "Move to Recycle Bin"],
            Labels(PrintMenuModel.For(note: false, link: false, capture: true, kept: false)));

    [Fact]
    public void A_note_speaks_of_its_text_and_has_no_Edit_or_Save() =>
        Assert.Equal(
            ["Copy text", "Open", "Show in Explorer", "-", "Keep", "Unpin", "Move to Recycle Bin"],
            Labels(PrintMenuModel.For(note: true, link: false, capture: true, kept: false)));

    [Fact]
    public void A_link_speaks_of_its_link() =>
        Assert.Equal(
            ["Copy link", "Open link", "Show in Explorer", "-", "Keep", "Unpin", "Move to Recycle Bin"],
            Labels(PrintMenuModel.For(note: true, link: true, capture: true, kept: false)));

    [Fact]
    public void A_kept_print_offers_to_stop_keeping_it()
    {
        var entries = PrintMenuModel.For(note: false, link: false, capture: false, kept: true);

        var keep = Assert.Single(entries, e => e.Action is PrintMenuAction.Keep or PrintMenuAction.StopKeeping);
        Assert.Equal(("Stop keeping", PrintMenuAction.StopKeeping), (keep.Label, keep.Action));
    }

    [Fact]
    public void Each_line_does_what_it_says()
    {
        var entries = PrintMenuModel.For(note: false, link: false, capture: true, kept: false);

        Assert.Equal(
            [
                PrintMenuAction.Copy, PrintMenuAction.Open, PrintMenuAction.Edit, PrintMenuAction.ShowInExplorer,
                PrintMenuAction.SaveToPictures, null, PrintMenuAction.Keep, PrintMenuAction.Unpin, PrintMenuAction.Recycle,
            ],
            entries.Select(e => e.Action));
    }

    [Fact]
    public void The_menu_follows_the_print()
    {
        var board = new Board();
        BoardFixture.Live(board, "snip", Origin.Capture, 0);
        board.PinNote(BoardFixture.NotePath("link"), new NoteBody("https://example.com", "https://example.com", "example.com", false), new Arrival.Live(BoardFixture.Start), BoardFixture.Now);
        var (note, capture) = (board.Prints[0], board.Prints[1]);
        board.SetKept(capture.Id, kept: true, BoardFixture.Now);

        Assert.Equal(Labels(PrintMenuModel.For(note: true, link: true, capture: true, kept: false)), Labels(PrintMenuModel.For(note)));
        Assert.Equal(
            Labels(PrintMenuModel.For(note: false, link: false, capture: true, kept: true)),
            Labels(PrintMenuModel.For(board.Find(capture.Id)!)));
    }
}
