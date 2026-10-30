using Tack.Core.Thumbnails;

namespace Tack.Core.Tests.Thumbnails;

/// <summary>A file's icon or thumbnail as Windows draws it, laid on white for its JPEG.</summary>
public class ShellPixelTests
{
    [Fact]
    public void A_premultiplied_icon_goes_on_white_paper()
    {
        // Opaque red, half-clear black (premultiplied: 0), and fully clear.
        byte[] pixels = [0, 0, 255, 255, 0, 0, 0, 128, 0, 0, 0, 0];

        PixelOps.PremultipliedOnWhite(pixels);

        Assert.Equal([0, 0, 255, 255, 127, 127, 127, 255, 255, 255, 255, 255], pixels);
    }

    [Fact]
    public void A_thumbnail_that_never_set_its_alpha_is_opaque_as_it_is()
    {
        byte[] pixels = [10, 20, 30, 0, 40, 50, 60, 0];

        PixelOps.PremultipliedOnWhite(pixels);

        Assert.Equal([10, 20, 30, 255, 40, 50, 60, 255], pixels);
    }
}
