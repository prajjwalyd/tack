using Tack.Core.Prints;
using Tack.Core.Storage;

namespace Tack.Core.Tests.Storage;

public class RestoreCheckTests
{
    static SavedPrint Image(string path) => new(path, Kind.Image, Origin.Folder, 1, null);

    static SavedPrint Note(string path) => new(path, Kind.Note, Origin.Capture, 1, null);

    [Fact]
    public void A_picture_that_is_there_is_tried()
    {
        using var scratch = new Scratch("restore-picture");
        var notes = scratch.Folder("Notes");

        Assert.Equal(Restoring.Try, RestoreCheck.Of(Image(scratch.File("a.png", "x")), notes));
        Assert.Equal(Restoring.Try, RestoreCheck.Of(Note(scratch.File(@"Notes\Note 1.txt", "x")), notes));
    }

    [Fact]
    public void A_file_that_is_gone_is_dropped_as_gone()
    {
        using var scratch = new Scratch("restore-gone");
        var notes = scratch.Folder("Notes");

        Assert.Equal(Restoring.Gone, RestoreCheck.Of(Image(scratch.File("missing.png")), notes));
        Assert.Equal(Restoring.Gone, RestoreCheck.Of(Image(scratch.File(@"no folder\missing.png")), notes));
        Assert.Equal(Restoring.Gone, RestoreCheck.Of(Note(scratch.File(@"Notes\missing.txt")), notes));
    }

    [Fact]
    public void Entries_no_tack_wrote_are_invalid()
    {
        using var scratch = new Scratch("restore-invalid");
        var notes = scratch.Folder("Notes");

        Assert.Equal(Restoring.Invalid, RestoreCheck.Of(Image(scratch.File("a.gif", "x")), notes));
        Assert.Equal(Restoring.Invalid, RestoreCheck.Of(Image(scratch.Folder("folder.png")), notes));
        Assert.Equal(Restoring.Invalid, RestoreCheck.Of(Note(scratch.File("elsewhere.txt", "x")), notes));
        Assert.Equal(Restoring.Invalid, RestoreCheck.Of(Note(scratch.File(@"Notes\a.png", "x")), notes));
    }
}
