using Tack.Core.Geometry;
using Tack.Core.Snips;
using Tack.Windows.Capture;

namespace Tack.Windows.Tests.Capture;

public sealed class ScreenPixelsTests
{
    [Fact]
    public unsafe void A_grab_reads_its_bgra_rows_by_screen_position()
    {
        var area = new Rect(100, 50, 2, 2);
        byte[] bits = [1, 2, 3, 0, 4, 5, 6, 0, 7, 8, 9, 0, 10, 11, 12, 0];

        fixed (byte* p = bits)
        {
            var grab = new ScreenPixels(area, p);

            Assert.Equal(new Rgb(3, 2, 1), grab.Get(100, 50));
            Assert.Equal(new Rgb(6, 5, 4), grab.Get(101, 50));
            Assert.Equal(new Rgb(12, 11, 10), grab.Get(101, 51));
            Assert.Null(grab.Get(102, 50));
            Assert.Null(grab.Get(100, 49));
        }
    }
}
