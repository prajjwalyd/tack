using Tack.Windows.Shell;

namespace Tack.Windows.Tests.Shell;

public class TrayGlyphTests
{
    [Theory]
    [InlineData(1.0, 16)]
    [InlineData(1.25, 20)]
    [InlineData(1.5, 24)]
    [InlineData(2.0, 32)]
    // 16 × 1.03125 is 16.5 px: 17, as Tack 0.2 rounded it.
    [InlineData(1.03125, 17)]
    public void The_icon_is_the_tray_size_at_the_scale(double scale, int size) =>
        Assert.Equal(size, TrayGlyph.SizeFor(scale));
}
