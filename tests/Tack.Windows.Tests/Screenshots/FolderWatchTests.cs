using System.Diagnostics;
using Tack.Windows.Screenshots;

namespace Tack.Windows.Tests.Screenshots;

public sealed class FolderWatchTests
{
    [Fact]
    public void A_new_file_arrives_then_changes()
    {
        using var dir = new Scratch("watch-create");
        using var watch = new FolderWatch(dir.Path);
        var reports = new Reports(watch);
        var file = dir.File("a.png");

        File.WriteAllBytes(file, [1]);
        reports.Expect(FolderChange.Arrived, file);
        reports.Expect(FolderChange.Changed, file);
        File.WriteAllBytes(file, [1, 2, 3, 4, 5, 6]);
        reports.Expect(FolderChange.Changed, file);
    }

    [Fact]
    public void A_rename_reports_the_old_name_gone_and_the_new_one_arrived()
    {
        using var dir = new Scratch("watch-rename");
        var (old, renamed) = (dir.File("a.png"), dir.File("b.png"));
        File.WriteAllBytes(old, [1]);
        using var watch = new FolderWatch(dir.Path);
        var reports = new Reports(watch);

        File.Move(old, renamed);
        reports.Expect(FolderChange.Gone, old);
        reports.Expect(FolderChange.Arrived, renamed);
    }

    [Fact]
    public void A_file_moved_in_arrives_and_moved_out_is_gone()
    {
        using var dir = new Scratch("watch-move");
        using var elsewhere = new Scratch("watch-move-elsewhere");
        var outside = elsewhere.File("a.png");
        File.WriteAllBytes(outside, [1]);
        using var watch = new FolderWatch(dir.Path);
        var reports = new Reports(watch);
        var inside = dir.File("b.png");

        File.Move(outside, inside);
        reports.Expect(FolderChange.Arrived, inside);
        File.Move(inside, outside);
        reports.Expect(FolderChange.Gone, inside);
    }

    [Fact]
    public void A_deleted_file_is_gone()
    {
        using var dir = new Scratch("watch-remove");
        var file = dir.File("a.png");
        File.WriteAllBytes(file, [1]);
        using var watch = new FolderWatch(dir.Path);
        var reports = new Reports(watch);

        File.Delete(file);
        reports.Expect(FolderChange.Gone, file);
    }

    [Fact]
    public void Subfolders_are_not_watched()
    {
        using var dir = new Scratch("watch-shallow");
        var inner = Directory.CreateDirectory(dir.File("inner")).FullName;
        using var watch = new FolderWatch(dir.Path);
        var reports = new Reports(watch);
        var top = dir.File("b.png");

        File.WriteAllBytes(Path.Combine(inner, "a.png"), [1]);
        File.WriteAllBytes(top, [1]);

        // The subfolder's own entry may report a change, never what is inside it.
        var end = DateTime.UtcNow.AddSeconds(1);
        while (end > DateTime.UtcNow && reports.Next(end - DateTime.UtcNow) is { } got)
        {
            Assert.True(got.Path == inner || got.Path == top, got.Path);
        }
    }

    [Fact]
    public void A_burst_of_files_is_all_reported()
    {
        using var dir = new Scratch("watch-burst");
        using var watch = new FolderWatch(dir.Path);
        var reports = new Reports(watch);
        var files = Enumerable.Range(0, 300).Select(i => dir.File($"shot {i}.png")).ToHashSet();

        foreach (var file in files)
        {
            File.WriteAllBytes(file, [1]);
        }

        var arrived = new HashSet<string>();
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!arrived.SetEquals(files))
        {
            Assert.True(end > DateTime.UtcNow, $"{files.Count - arrived.Count} files were not reported");
            if (reports.Next(end - DateTime.UtcNow) is (FolderChange.Arrived, var path))
            {
                arrived.Add(path);
            }
        }
    }

    [Fact]
    public void Disposing_stops_the_reports_at_once()
    {
        using var dir = new Scratch("watch-dispose");
        var watch = new FolderWatch(dir.Path);
        var reports = new Reports(watch);

        var stopping = Stopwatch.StartNew();
        watch.Dispose();
        Assert.True(stopping.Elapsed < TimeSpan.FromMilliseconds(500), $"{stopping.Elapsed}");

        File.WriteAllBytes(dir.File("a.png"), [1]);
        Assert.Null(reports.Next(TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void A_missing_folder_cannot_be_watched()
    {
        using var dir = new Scratch("watch-missing");
        Assert.Throws<DirectoryNotFoundException>(() => new FolderWatch(dir.File("nope")));
    }

    [Fact]
    public void A_deleted_folder_ends_the_watch()
    {
        using var dir = new Scratch("watch-deleted");
        using var watch = new FolderWatch(dir.Path);
        using var ended = new ManualResetEventSlim();
        watch.Ended += ended.Set;

        Directory.Delete(dir.Path, recursive: true);
        Assert.True(ended.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "the watch did not end");
    }

    [Fact]
    public void An_overflow_restarts_the_watch_and_it_keeps_reporting()
    {
        using var dir = new Scratch("watch-overflow");
        using var watch = new FolderWatch(dir.Path);
        using var held = new ManualResetEventSlim();
        using var restarted = new ManualResetEventSlim();
        watch.Restarted += restarted.Set;
        // Holding the first report up keeps the next read from being issued,
        // so the changes below pile up past the buffer.
        var first = true;
        watch.Changed += (_, _) =>
        {
            if (first)
            {
                first = false;
                held.Wait(TimeSpan.FromSeconds(10));
            }
        };
        var reports = new Reports(watch);
        var name = new string('x', 120);

        for (var i = 0; i < 600; i++)
        {
            File.WriteAllBytes(dir.File($"{name} {i}.png"), [1]);
        }
        held.Set();

        Assert.True(restarted.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "the overflow did not restart the watch");
        var after = dir.File("after.png");
        File.WriteAllBytes(after, [1]);
        reports.Expect(FolderChange.Arrived, after, seconds: 10);
    }
}
