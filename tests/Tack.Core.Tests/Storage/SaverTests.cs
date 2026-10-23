using Tack.Core.Prints;
using Tack.Core.Settings;
using Tack.Core.Storage;

namespace Tack.Core.Tests.Storage;

public class SaverTests
{
    static Snapshot SnapshotOf(long n) =>
        Snapshot.Of([], [new SavedPrint($@"C:\nowhere\{n}.png", Kind.Image, Origin.Folder, n, n)], new TackSettings());

    [Fact]
    public void A_burst_of_saves_writes_the_latest_a_few_times_at_most()
    {
        using var scratch = new Scratch("saver");
        var file = Path.Combine(scratch.Path, "board.json");
        var saver = Saver.Start(file, TimeSpan.FromMilliseconds(300));

        for (var n = 1; n <= 50; n++)
        {
            saver.Queue(SnapshotOf(n));
        }
        saver.Flush();
        var writes = saver.Writes;
        var (saved, _) = BoardFile.Load(file, 0);

        Assert.InRange(writes, 1, 3);
        Assert.Single(saved.Prints);
        Assert.Equal(50, saved.Prints[0].PinnedAt);
    }

    [Fact]
    public void A_failed_write_waits_to_be_tried_again()
    {
        using var scratch = new Scratch("saver-retry");
        // A folder where the file should be: every write fails.
        var file = scratch.Folder("board.json");
        var saver = Saver.Unthreaded(file);

        saver.Queue(SnapshotOf(7));
        saver.Flush();
        var kept = saver.HasWaiting;
        // Once the way is clear, the same snapshot goes through.
        Directory.Delete(file);
        saver.Flush();
        var (saved, _) = BoardFile.Load(file, 0);

        Assert.True(kept, "the snapshot is not lost when its write fails");
        Assert.Equal(7, saved.Prints[0].PinnedAt);
    }

    [Fact]
    public void A_write_that_throws_anything_is_tried_again()
    {
        using var scratch = new Scratch("saver-throws");
        var file = Path.Combine(scratch.Path, "board.json");
        var fail = true;
        var saver = Saver.Unthreaded(file, (to, snapshot) =>
        {
            if (fail)
            {
                throw new InvalidOperationException("not an I/O error");
            }
            BoardFile.Write(to, snapshot);
        });

        saver.Queue(SnapshotOf(9));
        saver.Flush();
        var kept = saver.HasWaiting;
        fail = false;
        saver.Flush();
        var (saved, _) = BoardFile.Load(file, 0);

        Assert.True(kept, "the snapshot waits to be tried again");
        Assert.Equal(9, saved.Prints[0].PinnedAt);
    }
}
