using Tack.Windows.Shell;

namespace Tack.Windows.Tests.Shell;

public class TrayGlyphTests
{
    [Theory]
    [InlineData(true, 1.0, "tray-light-16.png")]
    [InlineData(true, 1.25, "tray-light-20.png")]
    [InlineData(true, 1.5, "tray-light-24.png")]
    [InlineData(true, 1.75, "tray-light.png")]
    [InlineData(true, 2.0, "tray-light.png")]
    [InlineData(true, 2.5, "tray-light@2x.png")]
    [InlineData(false, 1.0, "tray-dark-16.png")]
    [InlineData(false, 1.25, "tray-dark-20.png")]
    [InlineData(false, 1.5, "tray-dark-24.png")]
    [InlineData(false, 2.0, "tray-dark.png")]
    [InlineData(false, 3.0, "tray-dark@2x.png")]
    public void The_glyph_matches_the_taskbar_theme_and_the_tray_size_as_in_0_2(bool light, double scale, string file) =>
        Assert.Equal(file, TrayGlyph.FileName(light, scale));

    [Fact]
    public void Halves_round_up_as_0_2_rounded_them()
    {
        // 16 × 1.03125 is 16.5 px: 17, so the 20 px glyph.
        Assert.Equal("tray-dark-20.png", TrayGlyph.FileName(false, 1.03125));
    }
}
