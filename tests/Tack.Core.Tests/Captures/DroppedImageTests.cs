using Tack.Core.Captures;

namespace Tack.Core.Tests.Captures;

public class DroppedImageTests
{
    [Fact]
    public void A_PNG_and_a_JPEG_are_told_by_their_first_bytes()
    {
        Assert.Equal("png", DroppedImage.Extension([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]));
        Assert.Equal("jpg", DroppedImage.Extension([0xFF, 0xD8, 0xFF, 0xE0]));
    }

    [Fact]
    public void Anything_else_is_refused_whatever_it_is_called()
    {
        Assert.Null(DroppedImage.Extension([]));
        Assert.Null(DroppedImage.Extension("GIF89a"u8));
        Assert.Null(DroppedImage.Extension([0x89, (byte)'P', (byte)'N', (byte)'G']));
        Assert.Null(DroppedImage.Extension([0xFF, 0xD8]));
    }

    [Fact]
    public void Up_to_40_MB_is_taken()
    {
        Assert.False(DroppedImage.TooLarge(40L * 1024 * 1024));
        Assert.True(DroppedImage.TooLarge(40L * 1024 * 1024 + 1));
    }
}
