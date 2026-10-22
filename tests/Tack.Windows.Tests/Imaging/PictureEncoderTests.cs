using Tack.Windows.Imaging;
using static Tack.Windows.Tests.Imaging.TestPictures;

namespace Tack.Windows.Tests.Imaging;

public sealed class PictureEncoderTests
{
    /// <summary>The PNG colour type in the IHDR chunk: 2 for RGB, 6 for RGB with alpha.</summary>
    static int ColourType(byte[] png) => png[25];

    [Fact]
    public async Task A_capture_without_transparency_saves_without_an_alpha_channel()
    {
        var png = await PictureEncoder.PngAsync(Busy(50, 40));

        Assert.True(IsPng(png));
        Assert.Equal(2, ColourType(png));
    }

    [Fact]
    public async Task Real_transparency_is_kept()
    {
        var picture = Busy(50, 40, (x, y) => (byte)(x * 5));

        var png = await PictureEncoder.PngAsync(picture);

        Assert.Equal(6, ColourType(png));
        Assert.Equal(picture.Bgra, (await PictureDecoder.DecodeAsync(png)).Bgra);
    }

    [Fact]
    public async Task A_jpeg_is_shrunk_to_its_long_side()
    {
        var jpeg = await PictureEncoder.JpegAsync(Busy(400, 1000), 360, 85);

        Assert.True(IsJpeg(jpeg));
        Assert.Equal((144, 360), await SizeOf(jpeg));
    }

    [Fact]
    public async Task Encoding_a_translucent_jpeg_leaves_the_picture_alone()
    {
        var picture = Solid(4, 4, 0, 0, 0, a: 0);

        await PictureEncoder.JpegAsync(picture, 360, 85);

        Assert.All(picture.Bgra, channel => Assert.Equal(0, channel));
    }

    [Fact]
    public void A_flat_alpha_becomes_opaque_and_real_alpha_stays()
    {
        var clear = Solid(2, 2, 1, 2, 3, a: 0).WithoutFlatAlpha();
        var mixed = Busy(2, 2, (x, y) => (byte)(x * 100)).WithoutFlatAlpha();

        Assert.True(clear.IsOpaque());
        Assert.Equal([3, 2, 1, 255], clear.Bgra[..4]);
        Assert.False(mixed.IsOpaque());
    }

    [Fact]
    public void A_picture_must_hold_its_pixels()
    {
        Assert.Throws<ArgumentException>(() => new Picture(2, 2, new byte[15]));
    }
}
