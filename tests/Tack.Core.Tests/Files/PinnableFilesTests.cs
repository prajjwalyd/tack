using Tack.Core.Files;

namespace Tack.Core.Tests.Files;

public class PinnableFilesTests
{
    [Fact]
    public void Only_pictures_on_a_drive_that_exist_are_pinned()
    {
        using var scratch = new Scratch("pinnable");
        var picture = scratch.File("a.png", "x");
        var photo = scratch.File("b.JPG", "x");

        var pinnable = PinnableFiles.Among(
        [
            picture,
            scratch.File("notes.txt", "x"),
            scratch.File("missing.png"),
            scratch.Folder("folder.png"),
            @"\\server\share\c.png",
            photo,
        ]);

        Assert.Equal([picture, photo], pinnable);
    }
}
