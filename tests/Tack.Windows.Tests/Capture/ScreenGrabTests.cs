using Tack.Core.Geometry;
using Tack.Core.Snips;
using Tack.Windows.Capture;
using Tack.Windows.Display;
using Tack.Windows.Imaging;

namespace Tack.Windows.Tests.Capture;

/// <summary>Against the real screen: a grab of it stands in for a snip.</summary>
// The two lookups grab the live screen and look for the grab again, so they
// fail whenever something on screen moves in between: they run only when asked
// for (dotnet test -- --explicit on), with a still screen.
public sealed class ScreenGrabTests
{
    /// <summary>A patch in the middle of the main monitor, as a snip of it would show.</summary>
    static (Rect Area, Picture Picture) Snip()
    {
        var monitor = Monitors.At(0, 0).Bounds;
        var area = new Rect(monitor.X + monitor.W / 2, monitor.Y + monitor.H / 2, 240, 160);
        var picture = ScreenGrab.GrabPicture(area);
        if (picture is null)
        {
            Assert.Skip("The screen cannot be read here.");
        }
        return (area, picture);
    }

    [Fact(Explicit = true)]
    public void A_snip_is_found_where_it_was_taken()
    {
        var (area, picture) = Snip();
        var pointer = (area.X + area.W, area.Y + area.H);

        var lookup = ScreenGrab.Find(picture, pointer);

        Assert.Equal(area, lookup.Found?.Rect);
        Assert.Equal(area, lookup.Rect);
        Assert.Equal(Source.Corner, lookup.Found?.Source);
        Assert.InRange(lookup.Tries, 1, 2);
    }

    [Fact(Explicit = true)]
    public void A_snip_is_found_from_a_thread_that_is_not_dpi_aware()
    {
        var (area, picture) = Snip();
        var pointer = (area.X, area.Y);

        var lookup = ScreenGrab.AsDpiUnaware(() => ScreenGrab.Find(picture, pointer));

        Assert.Equal(area, lookup.Found?.Rect);
    }

    [Fact]
    public void A_grab_is_opaque_and_the_size_asked_for()
    {
        var (_, picture) = Snip();

        Assert.Equal((240, 160), (picture.Width, picture.Height));
        Assert.True(picture.IsOpaque());
    }

    [Fact]
    public void Nothing_is_grabbed_of_an_empty_area()
    {
        Assert.Null(ScreenGrab.GrabPicture(new Rect(10, 10, 0, 5)));
    }
}
