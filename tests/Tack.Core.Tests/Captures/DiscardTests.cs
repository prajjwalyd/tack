using Tack.Core.Captures;
using Tack.Core.Prints;
using Tack.Core.Tests.Files;

namespace Tack.Core.Tests.Captures;

public class DiscardTests
{
    static Print PrintOf(string path, Origin origin) => new() { Id = "1", Path = path, Origin = origin };

    [Fact]
    public void Discarding_only_touches_captures_in_tacks_own_folder()
    {
        using var scratch = new Scratch("discard");
        var folder = scratch.Folder("Captures");
        var ours = scratch.File(@"Captures\ours.png", "x");
        var moved = scratch.File(@"Captures\saved to pictures.png", "x");
        var elsewhere = scratch.File("elsewhere.png", "x");
        scratch.Folder(@"Captures\folder.png");
        var bin = new NotingBin();

        Discard.File(PrintOf(ours, Origin.Capture), folder, bin);
        Discard.File(PrintOf(moved, Origin.Folder), folder, bin);
        Discard.File(PrintOf(elsewhere, Origin.Capture), folder, bin);
        // Not a file, and not plainly in the folder.
        Discard.File(PrintOf(Path.Combine(folder, "folder.png"), Origin.Capture), folder, bin);
        Discard.File(PrintOf(Path.Combine(folder, "..", "elsewhere.png"), Origin.Capture), folder, bin);

        Assert.Equal([ours], bin.Taken);
    }

    [Fact]
    public void Ageing_out_recycles_capture_files_only()
    {
        using var scratch = new Scratch("ageing");
        var screenshot = scratch.File("screenshot.png", "x");
        var capture = scratch.File("capture.png", "x");
        var board = new Board();
        var now = 1_800_000_000_000L;
        board.Pin(screenshot, new([], 10, 10, 1), Origin.Folder, new Arrival.Live(TimeSpan.Zero), now);
        board.Pin(capture, new([], 20, 20, 2), Origin.Capture, new Arrival.Live(TimeSpan.Zero), now);

        var aged = board.AgeOut(now + History.MaxAgeMs);
        var bin = new NotingBin();
        foreach (var print in aged)
        {
            Discard.File(print, scratch.Path, bin);
        }

        Assert.Equal(2, aged.Count);
        Assert.Equal([capture], bin.Taken);
    }
}
