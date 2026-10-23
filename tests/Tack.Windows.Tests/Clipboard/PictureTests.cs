using Tack.Windows.Clipboard;
using Tack.Windows.Imaging;
using Tack.Windows.Tests.Imaging;

namespace Tack.Windows.Tests.Clipboard;

public sealed class PictureTests
{
    [Fact]
    public async Task A_bitmap_off_the_clipboard_decodes_opaque()
    {
        // As Windows hands over a copied bitmap: a .bmp file, 32-bit BGRX,
        // bottom row first, with an alpha nobody set.
        var picture = Samples.Picture(255);
        var pixels = new List<byte>();
        for (var y = 2; y >= 0; y--)
        {
            for (var x = 0; x < 5; x++)
            {
                var at = (y * 5 + x) * 4;
                pixels.AddRange([picture.Bgra[at], picture.Bgra[at + 1], picture.Bgra[at + 2], 0]);
            }
        }
        Samples.AssertSame(picture, await new CopiedPicture(CopiedFormat.Bitmap, BmpFiles.Bgrx(5, 3, [.. pixels])).DecodeAsync());
    }

    [Fact]
    public async Task A_png_keeps_its_transparency()
    {
        var picture = Samples.Picture(128);
        Samples.AssertSame(picture, await new CopiedPicture(CopiedFormat.Png, await PictureEncoder.PngAsync(picture)).DecodeAsync());
    }

    [Fact]
    public async Task A_png_with_trailing_bytes_still_decodes()
    {
        // Clipboard memory can be bigger than what was put in it.
        var picture = Samples.Picture(255);
        byte[] png = [.. await PictureEncoder.PngAsync(picture), .. new byte[16]];
        Samples.AssertSame(picture, await new CopiedPicture(CopiedFormat.Png, png).DecodeAsync());
    }

    [Theory]
    [InlineData(CopiedFormat.Png)]
    [InlineData(CopiedFormat.Bitmap)]
    public async Task Garbage_does_not_decode(CopiedFormat format) =>
        Assert.Null(await new CopiedPicture(format, [1, 2, 3, 4, 5]).DecodeAsync());
}
