using Tack.Windows.Clipboard;
using Tack.Windows.Imaging;

namespace Tack.Windows.Tests.Clipboard;

/// <summary>Small made-up pictures for the clipboard tests.</summary>
static class Samples
{
    /// <summary>5 x 3 pixels: red across, green down, blue fixed, and one alpha for all.</summary>
    public static Picture Picture(byte alpha)
    {
        var bgra = new byte[5 * 3 * 4];
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                var at = (y * 5 + x) * 4;
                (bgra[at], bgra[at + 1], bgra[at + 2], bgra[at + 3]) = (200, (byte)(y * 80), (byte)(x * 50), alpha);
            }
        }
        return new Picture(5, 3, bgra);
    }

    public static void AssertSame(Picture expected, Picture? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        Assert.Equal(expected.Bgra, actual.Bgra);
    }
}
