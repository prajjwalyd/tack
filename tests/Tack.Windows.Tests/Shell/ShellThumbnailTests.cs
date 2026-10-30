using Tack.Windows.Imaging;
using Tack.Windows.Shell;

namespace Tack.Windows.Tests.Shell;

/// <summary>A linked file's face, as Explorer would draw it, and its type in Windows' words.</summary>
public sealed class ShellThumbnailTests
{
    [Fact]
    public async Task A_file_gets_a_jpeg_no_larger_than_320_a_side()
    {
        using var scratch = new Scratch("shell-thumb");
        var path = scratch.SmallFile("notes.txt");

        var thumb = await ShellThumbnail.MakeAsync(path);

        Assert.NotNull(thumb);
        Assert.Equal([0xFF, 0xD8], thumb.Jpeg[..2]);
        Assert.InRange(Math.Max(thumb.Width, thumb.Height), 1, ShellThumbnail.Side);
        Assert.Null(thumb.Hash);
        var decoded = await PictureDecoder.DecodeAsync(thumb.Jpeg);
        Assert.InRange(Math.Max(decoded.Width, decoded.Height), 1, ShellThumbnail.Side);
    }

    [Fact]
    public async Task A_folder_gets_its_icon()
    {
        using var scratch = new Scratch("shell-folder");

        var thumb = await ShellThumbnail.MakeAsync(scratch.Path);

        Assert.NotNull(thumb);
        Assert.InRange(Math.Max(thumb.Width, thumb.Height), 1, ShellThumbnail.Side);
    }

    [Fact]
    public async Task A_file_that_is_not_there_gets_nothing()
    {
        using var scratch = new Scratch("shell-gone");

        Assert.Null(await ShellThumbnail.MakeAsync(scratch.File("gone.docx")));
    }

    [Fact]
    public void A_type_is_named_from_the_extension_alone()
    {
        using var scratch = new Scratch("shell-type");

        // Worded by the PC's language and apps, so only that there are words, and that a folder's differ from a file's.
        Assert.NotEqual(string.Empty, ShellTypes.NameOf(scratch.File("never-written.txt"), folder: false));
        Assert.NotEqual(
            ShellTypes.NameOf(scratch.File("never-written.txt"), folder: false),
            ShellTypes.NameOf(scratch.Path, folder: true));
    }
}
