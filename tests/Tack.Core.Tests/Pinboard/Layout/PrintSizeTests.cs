using Tack.Core.Pinboard.Layout;

namespace Tack.Core.Tests.Pinboard.Layout;

/// <summary>Golden values from Tack 0.2's own photo rule, run as it shipped.</summary>
public class PrintSizeTests
{
    [Theory]
    [InlineData(1920, 1080, 160, 90)]
    [InlineData(1080, 1920, 56, 100)]
    [InlineData(100, 50, 160, 80)]
    [InlineData(10, 1000, 40, 100)]
    [InlineData(1000, 10, 160, 30)]
    [InlineData(0, 0, 160, 90)]
    [InlineData(-5, 20, 160, 90)]
    [InlineData(320, 200, 160, 100)]
    [InlineData(333, 211, 158, 100)]
    [InlineData(1366, 768, 160, 90)]
    [InlineData(500, 500, 100, 100)]
    [InlineData(17, 13, 131, 100)]
    public void A_photo_fits_160_by_100_and_never_shrinks_below_40_by_30(int w, int h, double expectedW, double expectedH)
    {
        Assert.Equal((expectedW, expectedH), PrintSize.Photo(w, h));
    }

    [Fact]
    public void A_print_adds_its_paper_border_and_a_note_is_one_size()
    {
        Assert.Equal((168, 98), PrintSize.Of(isNote: false, 1920, 1080));
        Assert.Equal((136, 86), PrintSize.Of(isNote: true, 1920, 1080));
        Assert.Equal((136, 86), PrintSize.Of(isNote: true, 0, 0));
    }
}
