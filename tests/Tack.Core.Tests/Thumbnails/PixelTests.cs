using Tack.Core.Thumbnails;

namespace Tack.Core.Tests.Thumbnails;

public class PixelTests
{
    /// <summary>A 5 x 3 picture whose pixels are (x, y, 7), in the given layout.</summary>
    static byte[] Picture(PixelLayout layout, int w = 5, int h = 3)
    {
        var bytes = new List<byte>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                bytes.AddRange(layout switch
                {
                    PixelLayout.Rgb24 => [(byte)x, (byte)y, 7],
                    PixelLayout.Rgba32 => [(byte)x, (byte)y, 7, 255],
                    _ => [7, (byte)y, (byte)x, 255],
                });
            }
        }
        return [.. bytes];
    }

    [Fact]
    public void The_same_pixels_hash_alike_in_any_layout_and_others_do_not()
    {
        var hash = PixelHash.Of(5, 3, Picture(PixelLayout.Rgba32), PixelLayout.Rgba32);

        Assert.Equal(hash, PixelHash.Of(5, 3, Picture(PixelLayout.Rgb24), PixelLayout.Rgb24));
        Assert.Equal(hash, PixelHash.Of(5, 3, Picture(PixelLayout.Bgra32), PixelLayout.Bgra32));
        var other = Picture(PixelLayout.Rgba32);
        other[^4] = 9;
        Assert.NotEqual(hash, PixelHash.Of(5, 3, other, PixelLayout.Rgba32));
        // The same bytes in another shape are another picture.
        Assert.NotEqual(hash, PixelHash.Of(3, 5, Picture(PixelLayout.Rgba32), PixelLayout.Rgba32));
        Assert.Equal(PixelHash.Of(0, 0, [], PixelLayout.Rgb24), PixelHash.Of(0, 0, [], PixelLayout.Rgba32));
    }

    [Fact]
    public void Transparency_goes_on_white_paper()
    {
        byte[] clear = [0, 0, 0, 0];
        byte[] half = [0, 0, 0, 128];

        PixelOps.FlattenOnWhite(clear);
        PixelOps.FlattenOnWhite(half);

        Assert.Equal([255, 255, 255, 255], clear);
        Assert.Equal([127, 127, 127, 255], half);
    }

    [Fact]
    public void Flat_alpha_is_dropped()
    {
        Assert.True(PixelOps.HasFlatAlpha([1, 2, 3, 255, 4, 5, 6, 255]));
        Assert.True(PixelOps.HasFlatAlpha([1, 2, 3, 0, 4, 5, 6, 0]));
        Assert.False(PixelOps.HasFlatAlpha([1, 2, 3, 255, 4, 5, 6, 128]));
    }

    [Fact]
    public void The_drag_image_is_premultiplied()
    {
        byte[] pixel = [200, 100, 50, 128];

        PixelOps.Premultiply(pixel);

        Assert.Equal([100, 50, 25, 128], pixel);
    }

    [Fact]
    public void Thumbnails_fit_their_box_and_keep_the_picture_shape()
    {
        Assert.Equal((360, 180), ThumbnailRules.Fit(1200, 600, ThumbnailRules.ThumbSide));
        Assert.Equal((160, 80), ThumbnailRules.Fit(360, 180, 160));
        Assert.Equal((1600, 800), ThumbnailRules.Fit(3200, 1600, ThumbnailRules.FlightSide));
        // Already small enough: kept at its size.
        Assert.Equal((200, 100), ThumbnailRules.Fit(200, 100, ThumbnailRules.ThumbSide));
        // A sliver never shrinks to nothing.
        Assert.Equal((360, 1), ThumbnailRules.Fit(4000, 2, ThumbnailRules.ThumbSide));
    }
}
