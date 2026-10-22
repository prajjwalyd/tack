using Tack.Core.Geometry;
using Tack.Core.Snips;
using Tack.Windows.Capture;

namespace Tack.Windows.Tests.Capture;

public sealed class LooksTests
{
    static readonly Found Telling = new(new Rect(10, 20, 300, 200), Source.Corner, Corner.BottomRight, 1.0, 40.0);
    static readonly Found Plain = new(new Rect(10, 20, 300, 200), Source.Corner, Corner.BottomRight, 1.0, 2.0);
    static readonly Found Whole = new(new Rect(15, 25, 300, 200), Source.Corner, Corner.TopLeft, 0.99, 30.0);

    /// <summary>Takes the looks with <paramref name="quick"/> and <paramref name="whole"/> as their answers, noting each look and any wait.</summary>
    static (Found? Found, int Tries, List<string> Steps) Take(Found? quick, Found? whole)
    {
        var steps = new List<string>();
        var (found, tries) = Looks.Take(
            isQuick =>
            {
                steps.Add(isQuick ? "quick" : "whole");
                return isQuick ? quick : whole;
            },
            () => steps.Add("wait"));
        return (found, tries, steps);
    }

    [Fact]
    public void A_telling_quick_look_settles_it()
    {
        var (found, tries, steps) = Take(Telling, Whole);

        Assert.Equal((Telling, 1), (found, tries));
        Assert.Equal(["quick"], steps);
    }

    [Fact]
    public void A_plain_quick_look_is_checked_against_the_whole_picture_at_once()
    {
        var (found, tries, steps) = Take(Plain, Whole);
        Assert.Equal((Whole, 2), (found, tries));
        Assert.Equal(["quick", "whole"], steps);
        // The whole picture found nothing: the plain match is still the best guess.
        Assert.Equal(Plain, Take(Plain, null).Found);
    }

    [Fact]
    public void Nothing_found_waits_for_the_overlay_to_go_before_looking_again()
    {
        var (found, tries, steps) = Take(null, Whole);

        Assert.Equal((Whole, 2), (found, tries));
        Assert.Equal(["quick", "wait", "whole"], steps);
        Assert.Null(Take(null, null).Found);
    }

    [Fact]
    public void A_print_flies_from_where_its_picture_was_found()
    {
        Assert.Equal(Telling.Rect, Looks.FlyFrom(Telling, (1000, 500), 300, 200, 1.5));
    }

    [Fact]
    public void A_print_not_found_flies_from_a_small_rectangle_on_the_pointer_scaled_with_the_monitor()
    {
        Assert.Equal(new Rect(1000 - 72, 500 - 36, 144, 72), Looks.FlyFrom(null, (1000, 500), 1200, 600, 1.5));
        Assert.Equal(new Rect(1000 - 24, 500 - 48, 48, 96), Looks.FlyFrom(null, (1000, 500), 600, 1200, 1.0));
    }
}
