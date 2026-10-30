using Tack.Core.Files;
using Tack.Core.Prints;
using Tack.Core.Storage;

namespace Tack.Core.Tests.Storage;

/// <summary>Linked files and folders at startup, and while the board runs.</summary>
public class RestoreCheckFileTests
{
    static SavedPrint Linked(string path) => new(path, Kind.File, Origin.Folder, 1, null);

    [Fact]
    public void A_file_or_folder_that_is_there_is_tried()
    {
        using var scratch = new Scratch("restore-file");
        var notes = scratch.Folder("Notes");

        Assert.Equal(Restoring.Try, RestoreCheck.Of(Linked(scratch.File("Report.docx", "x")), notes));
        Assert.Equal(Restoring.Try, RestoreCheck.Of(Linked(scratch.File("shot.png", "x")), notes));
        Assert.Equal(Restoring.Try, RestoreCheck.Of(Linked(scratch.Folder("Projects")), notes));
    }

    [Fact]
    public void A_file_or_folder_that_went_is_dropped_as_gone()
    {
        using var scratch = new Scratch("restore-file-gone");
        var notes = scratch.Folder("Notes");

        Assert.Equal(Restoring.Gone, RestoreCheck.Of(Linked(scratch.File("moved.docx")), notes));
        Assert.Equal(Restoring.Gone, RestoreCheck.Of(Linked(scratch.File(@"gone folder\a.zip")), notes));
    }

    [Theory]
    [InlineData(@"\\server\share\a.docx")]
    [InlineData(@"\\?\C:\a.docx")]
    [InlineData(@"relative\a.docx")]
    [InlineData("")]
    public void A_file_off_a_drive_letter_is_invalid(string path) =>
        Assert.Equal(Restoring.Invalid, RestoreCheck.Of(Linked(path), @"C:\Notes"));

    [Fact]
    public void A_picture_entry_that_is_a_folder_stays_invalid()
    {
        using var scratch = new Scratch("restore-folder-image");

        Assert.Equal(Restoring.Invalid, RestoreCheck.Of(new SavedPrint(scratch.Folder("x.png"), Kind.Image, Origin.Folder, 1, null), scratch.Path));
    }

    [Fact]
    public void A_linked_file_is_gone_once_moved_or_deleted_but_not_while_its_drive_is_away()
    {
        using var scratch = new Scratch("linked-gone");
        var file = scratch.File("a.txt", "x");
        var folder = scratch.Folder("b");

        Assert.False(LinkedFile.IsGone(file));
        Assert.False(LinkedFile.IsGone(folder));
        File.Delete(file);
        Directory.Delete(folder);
        Assert.True(LinkedFile.IsGone(file));
        Assert.True(LinkedFile.IsGone(folder));
        Assert.False(LinkedFile.IsGone(NoDrive() + @":\stick\a.txt"));
    }

    /// <summary>A drive letter with no drive behind it.</summary>
    static char NoDrive()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        return Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).Last(c => !used.Contains(c));
    }
}
