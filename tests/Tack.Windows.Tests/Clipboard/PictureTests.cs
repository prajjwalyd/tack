using System.Buffers.Binary;
using Tack.Windows.Clipboard;
using Tack.Windows.Imaging;

namespace Tack.Windows.Tests.Clipboard;

public sealed class PictureTests
{
    [Fact]
    public async Task A_bitmap_put_on_the_clipboard_has_the_same_pixels_the_right_way_up()
    {
        var picture = Samples.Picture(255);
        var dib = DibV5.Of(picture);
        // The colour masks sit inside the header only, as other apps expect.
        Assert.Equal(124 + 5 * 3 * 4, dib.Length);
        Samples.AssertSame(picture, await new CopiedPicture(CopiedFormat.Dib, dib).DecodeAsync());
    }

    [Fact]
    public async Task A_bitmap_put_on_the_clipboard_keeps_its_transparency()
    {
        var picture = Samples.Picture(128);
        Samples.AssertSame(picture, await new CopiedPicture(CopiedFormat.Dib, DibV5.Of(picture)).DecodeAsync());
    }

    [Fact]
    public async Task A_plain_bitmap_off_the_clipboard_decodes()
    {
        // CF_DIB as Windows makes it: BITMAPINFOHEADER, 32-bit BGRX, bottom row first.
        var picture = Samples.Picture(255);
        var dib = new List<byte>();
        foreach (var value in new uint[] { 40, 5, 3, 1 | (32 << 16), 0 /* BI_RGB */, 60, 0, 0, 0, 0 })
        {
            var word = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(word, value);
            dib.AddRange(word);
        }
        for (var y = 2; y >= 0; y--)
        {
            for (var x = 0; x < 5; x++)
            {
                var at = (y * 5 + x) * 4;
                dib.AddRange([picture.Bgra[at], picture.Bgra[at + 1], picture.Bgra[at + 2], 0]);
            }
        }
        Samples.AssertSame(picture, await new CopiedPicture(CopiedFormat.Dib, [.. dib]).DecodeAsync());
    }

    [Fact]
    public async Task A_png_with_trailing_bytes_still_decodes()
    {
        // Clipboard memory can be bigger than what was put in it.
        var picture = Samples.Picture(255);
        byte[] png = [.. await PictureEncoder.PngAsync(picture), .. new byte[16]];
        Samples.AssertSame(picture, await new CopiedPicture(CopiedFormat.Png, png).DecodeAsync());
    }

    [Fact]
    public async Task Garbage_does_not_decode() =>
        Assert.Null(await new CopiedPicture(CopiedFormat.Png, [1, 2, 3, 4, 5]).DecodeAsync());
}
