using Tack.Core.Captures;
using Tack.Core.Tests.Files;

namespace Tack.Core.Tests.Captures;

public class SweepTests
{
    /// <summary>Nine days from now: every file a test just wrote is past the grace.</summary>
    static DateTime NineDaysOn() => Sweep.Cutoff(DateTime.UtcNow.AddDays(9), boardSaved: null);

    [Fact]
    public void The_sweep_takes_only_old_unpinned_images()
    {
        using var scratch = new Scratch("sweep");
        var pinned = scratch.File("Pinned.png", "x");
        var leftover = scratch.File("Leftover.png", "x");
        var partial = scratch.File("Crashed.png.part", "x");
        // Not an image, so not a capture Tack wrote.
        scratch.File("notes.txt", "x");
        // Pinned under another case: Windows paths ignore it.
        var pinnedUpper = pinned.ToUpperInvariant();

        var bin = new NotingBin();
        var swept = Sweep.Captures(scratch.Path, [pinnedUpper], NineDaysOn(), bin);
        // A day from now, the same leftover is far too young to touch.
        var young = new NotingBin();
        var sweptSoon = Sweep.Captures(scratch.Path, [pinnedUpper], Sweep.Cutoff(DateTime.UtcNow.AddDays(1), null), young);

        Assert.Equal([partial, leftover], swept.Order(StringComparer.Ordinal));
        Assert.Equal(2, bin.Taken.Count);
        Assert.Empty(sweptSoon);
        Assert.Empty(young.Taken);
    }

    [Fact]
    public void Nothing_written_after_board_json_was_last_saved_is_swept()
    {
        using var scratch = new Scratch("sweep-after-save");
        var saved = DateTime.UtcNow.AddMinutes(-1);
        scratch.File("Unlisted.png", "x");
        var later = DateTime.UtcNow.AddDays(9);

        var swept = Sweep.Captures(scratch.Path, [], Sweep.Cutoff(later, saved), new NotingBin());

        Assert.Empty(swept);
        Assert.Equal(later - Sweep.Grace, Sweep.Cutoff(later, later));
    }

    [Fact]
    public void A_pinned_file_counts_however_its_folder_is_spelled()
    {
        using var scratch = new Scratch("sweep-spelling");
        const string Name = "Screenshot 2026-10-08 023223.png";
        var pinned = scratch.File(Name, "x");
        var elsewhere = scratch.Folder("Elsewhere");
        string[] spellings =
        [
            @"\\?\" + pinned,
            Path.Combine(scratch.Path + Path.DirectorySeparatorChar, Name),
            Path.Combine(elsewhere, "..", Name.ToUpperInvariant()),
            // A folder that cannot be found: the name alone is kept, to be safe.
            Path.Combine(@"Q:\gone", Name),
        ];

        var taken = spellings.Select(spelling =>
        {
            var bin = new NotingBin();
            Sweep.Captures(scratch.Path, [spelling], NineDaysOn(), bin);
            return bin.Taken.Count;
        }).ToArray();
        // The same name in another folder that exists does not count.
        var other = new NotingBin();
        Sweep.Captures(scratch.Path, [Path.Combine(elsewhere, Name)], NineDaysOn(), other);

        Assert.Equal([0, 0, 0, 0], taken);
        Assert.Equal([pinned], other.Taken);
    }

    [Fact]
    public void Links_are_never_swept_and_a_linked_folder_not_at_all()
    {
        using var scratch = new Scratch("sweep-links");
        var real = scratch.Folder("Real");
        var target = scratch.File(@"Real\a.png", "x");
        // Creating links needs Developer Mode or elevation; without it there is nothing to test.
        var fileLink = TryLink(() => File.CreateSymbolicLink(Path.Combine(scratch.Path, "link.png"), target));
        var folderLink = TryLink(() => Directory.CreateSymbolicLink(Path.Combine(scratch.Path, "Linked"), real));
        Assert.SkipUnless(fileLink || folderLink, "This account cannot create links.");

        var bin = new NotingBin();
        Sweep.Captures(scratch.Path, [], NineDaysOn(), bin);
        var through = new NotingBin();
        Sweep.Captures(Path.Combine(scratch.Path, "Linked"), [], NineDaysOn(), through);

        if (fileLink)
        {
            Assert.Empty(bin.Taken);
        }
        if (folderLink)
        {
            Assert.Empty(through.Taken);
        }
    }

    static bool TryLink(Action link)
    {
        try
        {
            link();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
