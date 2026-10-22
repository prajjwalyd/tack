using Tack.Core.Thumbnails;
using Tack.Windows.Imaging;
using static Tack.Windows.Tests.Imaging.TestPictures;

namespace Tack.Windows.Tests.Imaging;

public sealed class PictureDecoderTests
{
    [Fact]
    public async Task A_png_file_decodes_to_its_own_pixels()
    {
        using var scratch = new Scratch("decode");
        var picture = Busy(33, 17, (x, y) => (byte)(x * 7));
        var path = scratch.File("picture.png");
        await File.WriteAllBytesAsync(path, await PictureEncoder.PngAsync(picture), TestContext.Current.CancellationToken);

        var decoded = await PictureDecoder.DecodeFileAsync(path);

        Assert.Equal((33, 17), (decoded.Width, decoded.Height));
        Assert.Equal(picture.Bgra, decoded.Bgra);
    }

    [Fact]
    public async Task A_jpeg_file_decodes()
    {
        using var scratch = new Scratch("decode");
        var path = scratch.File("picture.jpg");
        await File.WriteAllBytesAsync(path, await PictureEncoder.JpegAsync(Solid(40, 30, 200, 100, 50), 360, 85), TestContext.Current.CancellationToken);

        var decoded = await PictureDecoder.DecodeFileAsync(path);

        Assert.Equal((40, 30), (decoded.Width, decoded.Height));
        var (b, g, r, a) = (decoded.Bgra[0], decoded.Bgra[1], decoded.Bgra[2], decoded.Bgra[3]);
        Assert.InRange(r, 195, 205);
        Assert.InRange(g, 95, 105);
        Assert.InRange(b, 45, 55);
        Assert.Equal(255, a);
    }

    [Fact]
    public async Task A_capture_and_its_png_file_hash_alike()
    {
        var picture = Busy(300, 200);

        var decoded = await PictureDecoder.DecodeAsync(await PictureEncoder.PngAsync(picture));

        Assert.Equal(picture.Hash(), decoded.Hash());
        Assert.Equal(PixelHash.Of(300, 200, picture.Bgra, PixelLayout.Bgra32), picture.Hash());
    }

    [Fact]
    public async Task Only_png_and_jpeg_are_decoded()
    {
        var bitmap = BitmapFile.For(Dibs.Bgrx(2, 1, [1, 2, 3, 0, 4, 5, 6, 0]));

        await Assert.ThrowsAsync<InvalidDataException>(() => PictureDecoder.DecodeAsync(bitmap));
        await Assert.ThrowsAsync<InvalidDataException>(() => PictureDecoder.DecodeAsync("not a picture"u8.ToArray()));
        await Assert.ThrowsAsync<InvalidDataException>(() => PictureDecoder.DecodeAsync([]));
    }

    [Fact]
    public async Task A_broken_png_is_refused()
    {
        var png = await PictureEncoder.PngAsync(Busy(64, 64));

        await Assert.ThrowsAsync<InvalidDataException>(() => PictureDecoder.DecodeAsync(png[..(png.Length / 2)]));
    }

    [Fact]
    public async Task A_png_file_still_being_written_is_refused()
    {
        using var scratch = new Scratch("decode");
        var png = await PictureEncoder.PngAsync(Busy(64, 64));
        var path = scratch.File("half.png");
        await File.WriteAllBytesAsync(path, png[..(png.Length - 20)], TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => PictureDecoder.DecodeFileAsync(path));
    }

    [Fact]
    public async Task A_picture_wider_than_the_limit_is_refused()
    {
        var png = await PictureEncoder.PngAsync(Solid(ThumbnailRules.MaxSide + 1, 1, 9, 9, 9));

        await Assert.ThrowsAsync<InvalidDataException>(() => PictureDecoder.DecodeAsync(png));
    }

    [Fact]
    public async Task A_bitmap_off_the_clipboard_decodes_top_down_and_opaque()
    {
        // Bottom-up rows, as most bitmaps are, with an alpha nobody set.
        byte[] pixels =
        [
            255, 0, 0, 0, 255, 255, 255, 0, // bottom: blue, white
            0, 0, 255, 0, 0, 255, 0, 0, // top: red, green
        ];

        var decoded = await PictureDecoder.DecodeDibAsync(Dibs.Bgrx(2, 2, pixels));

        Assert.Equal((2, 2), (decoded.Width, decoded.Height));
        Assert.Equal([0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255], decoded.Bgra);
    }

    [Fact]
    public async Task A_24_bit_bitmap_with_padded_rows_decodes()
    {
        // Three pixels make 9 bytes, padded to 12.
        byte[] row = [10, 20, 30, 40, 50, 60, 70, 80, 90, 0, 0, 0];

        var decoded = await PictureDecoder.DecodeDibAsync(Dibs.Of(3, 1, bits: 24, compression: 0, pixels: row));

        Assert.Equal([10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255], decoded.Bgra);
    }

    [Fact]
    public async Task A_bitmap_with_colour_masks_reads_its_pixels_past_them()
    {
        // BI_BITFIELDS: red, green and blue masks between the header and the pixels; top-down rows.
        byte[] masks = [0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0];
        byte[] pixels = [10, 20, 30, 0, 40, 50, 60, 0];

        var decoded = await PictureDecoder.DecodeDibAsync(Dibs.Of(2, -1, bits: 32, compression: 3, pixels: [.. masks, .. pixels]));

        Assert.Equal([10, 20, 30, 255, 40, 50, 60, 255], decoded.Bgra);
    }
}
