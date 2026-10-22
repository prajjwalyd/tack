using Tack.Core.Pinboard.Input;

namespace Tack.Core.Tests.Pinboard.Input;

public class ScrollWheelTests
{
    [Theory]
    [InlineData(0, 100, 100, true)]
    [InlineData(0, -120, -120, true)]
    [InlineData(0, 7.5, 7.5, false)]
    [InlineData(0, 49, 49, false)]
    [InlineData(0, 50.5, 50.5, false)]
    [InlineData(-60, 20, -60, false)]
    [InlineData(10, 100, 100, false)]
    public void Pixels_are_a_notch_only_in_big_round_steps_on_one_axis(double dx, double dy, double delta, bool notch) =>
        Assert.Equal(new WheelTurn(delta, notch), ScrollWheel.Read(dx, dy, WheelMode.Pixels, 400));

    [Fact]
    public void Lines_and_pages_are_always_notches() =>
        Assert.Equal(
            [new WheelTurn(120, true), new WheelTurn(-400, true), new WheelTurn(0.5 * 40, true)],
            new[] { ScrollWheel.Read(0, 3, WheelMode.Lines, 400), ScrollWheel.Read(0, -1, WheelMode.Pages, 400), ScrollWheel.Read(0, 0.5, WheelMode.Lines, 400) });

    [Fact]
    public void A_wheel_notch_on_Windows_is_100_px_with_three_lines_as_0_2_saw_it()
    {
        // Forward, away from the user, goes back towards the row's start.
        Assert.Equal(new WheelTurn(-100, true), ScrollWheel.FromWindows(120, horizontal: false, lines: 3, touchpad: false, pageWidth: 400));
        Assert.Equal(new WheelTurn(200, true), ScrollWheel.FromWindows(-240, horizontal: false, lines: 3, touchpad: false, pageWidth: 400));
        Assert.Equal(new WheelTurn(-100.0 / 3, true), ScrollWheel.FromWindows(120, horizontal: false, lines: 1, touchpad: false, pageWidth: 400));
    }

    [Fact]
    public void A_page_setting_scrolls_a_view_per_notch() =>
        Assert.Equal(new WheelTurn(400, true), ScrollWheel.FromWindows(-120, horizontal: false, lines: -1, touchpad: false, pageWidth: 400));

    [Fact]
    public void Fine_steps_sideways_steps_and_touchpads_are_not_notches()
    {
        Assert.Equal(new WheelTurn(-25, false), ScrollWheel.FromWindows(30, horizontal: false, lines: 3, touchpad: false, pageWidth: 400));
        Assert.Equal(new WheelTurn(100, false), ScrollWheel.FromWindows(120, horizontal: true, lines: 3, touchpad: false, pageWidth: 400));
        Assert.Equal(new WheelTurn(-100, false), ScrollWheel.FromWindows(120, horizontal: false, lines: 3, touchpad: true, pageWidth: 400));
    }

    [Fact]
    public void No_turn_or_no_lines_moves_nothing()
    {
        Assert.Equal(0, ScrollWheel.FromWindows(0, horizontal: false, lines: 3, touchpad: false, pageWidth: 400).Delta);
        Assert.Equal(0, ScrollWheel.FromWindows(120, horizontal: false, lines: 0, touchpad: false, pageWidth: 400).Delta);
    }
}
