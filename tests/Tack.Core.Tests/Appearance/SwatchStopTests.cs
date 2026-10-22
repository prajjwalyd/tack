using Tack.Core.Appearance;
using Tack.Core.Pinboard.Look;

namespace Tack.Core.Tests.Appearance;

public class SwatchStopTests
{
    static readonly SwatchStop[] Stops =
    [
        new(0, new Rgba(0, 0, 0, 0)),
        new(0.5, new Rgba(200, 100, 50, 1)),
        new(1, new Rgba(255, 255, 255, 0.5)),
    ];

    [Fact]
    public void A_stop_is_its_own_colour() =>
        Assert.Equal(new Rgba(200, 100, 50, 1), SwatchStop.Sample(Stops, 0.5));

    [Fact]
    public void Between_two_stops_the_colour_and_its_opacity_are_mixed_evenly()
    {
        Assert.Equal(new Rgba(100, 50, 25, 0.5), SwatchStop.Sample(Stops, 0.25));
        Assert.Equal(new Rgba(227.5, 177.5, 152.5, 0.75), SwatchStop.Sample(Stops, 0.75));
    }

    [Fact]
    public void Past_either_end_is_the_end()
    {
        Assert.Equal(Stops[0].Colour, SwatchStop.Sample(Stops, -1));
        Assert.Equal(Stops[2].Colour, SwatchStop.Sample(Stops, 2));
    }

    [Fact]
    public void The_custom_wheel_goes_round_to_where_it_started()
    {
        var wheel = FeltChoices.Wheel;
        Assert.Equal(7, wheel.Count);
        Assert.Equal(wheel[0].Colour, wheel[^1].Colour);
        Assert.Equal("#f2493a", wheel[0].Colour.Rgb.ToHex());
        Assert.Equal("#b34ae0", wheel[5].Colour.Rgb.ToHex());
        Assert.Equal(1, wheel[^1].At);
    }
}
