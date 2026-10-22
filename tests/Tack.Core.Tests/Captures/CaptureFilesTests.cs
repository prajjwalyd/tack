using Tack.Core.Captures;

namespace Tack.Core.Tests.Captures;

public class CaptureFilesTests
{
    static readonly Timestamp At = new(2026, 1, 2, 3, 4, 5);

    [Fact]
    public void Captures_are_named_like_snipping_tool_files() =>
        Assert.Equal("Screenshot 2026-10-07 090503", new Timestamp(2026, 10, 7, 9, 5, 3).FileStem());

    [Fact]
    public void The_same_pixels_soon_after_are_a_repeat()
    {
        var filter = new RepeatFilter();
        var now = TimeSpan.FromHours(1);

        Assert.False(filter.IsRepeat(10, now));
        Assert.True(filter.IsRepeat(10, now + TimeSpan.FromSeconds(1)));
        // Each read restarts the window.
        Assert.True(filter.IsRepeat(10, now + TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Other_pixels_or_a_later_copy_are_new()
    {
        var filter = new RepeatFilter();
        var now = TimeSpan.FromHours(1);

        Assert.False(filter.IsRepeat(10, now));
        Assert.False(filter.IsRepeat(20, now));
        Assert.False(filter.IsRepeat(20, now + RepeatFilter.Window));
    }

    [Fact]
    public void Saving_twice_in_a_second_picks_a_free_name()
    {
        using var scratch = new Scratch("capture-save");

        var first = ReservedFiles.SaveBytes([1], scratch.Path, "Screenshot", "png", At);
        var second = ReservedFiles.SaveBytes([2], scratch.Path, "Screenshot", "png", At);

        Assert.Equal("Screenshot 2026-01-02 030405.png", Path.GetFileName(first));
        Assert.Equal("Screenshot 2026-01-02 030405 (2).png", Path.GetFileName(second));
    }

    [Fact]
    public void A_reserved_name_is_taken_until_written_and_then_moved_into_place()
    {
        using var scratch = new Scratch("capture-reserve");

        var first = ReservedFiles.Reserve(scratch.Path, At);
        var second = ReservedFiles.Reserve(scratch.Path, At);
        var (firstExists, partialExists) = (File.Exists(first), File.Exists(ReservedFiles.PartialPath(first)));
        ReservedFiles.WriteReserved([1, 2, 3], first);
        var written = File.Exists(first) && !File.Exists(ReservedFiles.PartialPath(first));
        var third = ReservedFiles.Reserve(scratch.Path, At);

        Assert.True(!firstExists && partialExists, "only the partial file exists before the write");
        Assert.Equal("Screenshot 2026-01-02 030405 (2).png", Path.GetFileName(second));
        Assert.True(written);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(first));
        Assert.Equal("Screenshot 2026-01-02 030405 (3).png", Path.GetFileName(third));
    }
}
