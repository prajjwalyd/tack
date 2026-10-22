using Tack.Core.Geometry;
using Tack.Core.Settings;

namespace Tack.Core.Tests.Settings;

/// <summary>Where the Settings window goes: under the board, on the board's monitor.</summary>
public class SettingsPlacementTests
{
    /// <summary>A 1080p monitor's work area at a scale, the taskbar 40 logical px high.</summary>
    static Rect Work1080(double scale) =>
        new(0, 0, 1920, 1080 - (int)Math.Round(40.0 * scale, MidpointRounding.AwayFromZero));

    /// <summary>Frame of a normal window: 16 px of borders and a 39 px title bar at 100%.</summary>
    static (int W, int H) Frame(double scale) =>
        ((int)Math.Round(16.0 * scale, MidpointRounding.AwayFromZero), (int)Math.Round(39.0 * scale, MidpointRounding.AwayFromZero));

    static int Px(double logical, double scale) => (int)Math.Round(logical * scale, MidpointRounding.AwayFromZero);

    [Fact]
    public void At_100_percent_it_sits_under_the_board_at_full_height()
    {
        var at = SettingsPlacement.For(Work1080(1.0), 1.0, Frame(1.0));
        Assert.Equal((520 + 16, 640 + 39), (at.W, at.H));
        // The board's strip, then a gap.
        Assert.Equal(176 + 16, at.Y);
        Assert.Equal((1920 - 536) / 2, at.X);
    }

    [Fact]
    public void At_150_percent_on_1080p_it_is_cut_to_fit_and_clear_of_the_board()
    {
        var (scale, chrome) = (1.5, Frame(1.5));
        var work = Work1080(scale);
        var at = SettingsPlacement.For(work, scale, chrome);
        Assert.True(at.Y >= Px(176, scale) + Px(16, scale), at.ToString());
        // A gap above the taskbar.
        Assert.Equal(work.Y + work.H - Px(16, scale), at.Y + at.H);
        var page = (at.H - chrome.H) / scale;
        Assert.InRange(page, 420.0, 639.99);
        Assert.Equal(Px(520, scale) + chrome.W, at.W);
    }

    [Fact]
    public void A_second_monitor_places_it_in_its_own_work_area()
    {
        // A monitor to the right at 200%, 3840x2160, taskbar 80 px at the bottom.
        var at = SettingsPlacement.For(new Rect(1920, 0, 3840, 2160 - 80), 2.0, Frame(2.0));
        Assert.Equal(1040 + 32, at.W);
        Assert.Equal(1920 + (3840 - at.W) / 2, at.X);
        Assert.Equal(384, at.Y);
        // Room for all of it.
        Assert.Equal(1280 + 78, at.H);
        // One above a primary monitor, its work area starting below zero.
        at = SettingsPlacement.For(new Rect(0, -1080, 1920, 1040), 1.0, Frame(1.0));
        Assert.Equal(-1080 + 176 + 16, at.Y);
    }

    [Fact]
    public void A_short_screen_overlaps_the_board_rather_than_leaving_the_screen()
    {
        // 560 high: only 352 px under the board; the page keeps 420.
        var at = SettingsPlacement.For(new Rect(0, 0, 1024, 560), 1.0, Frame(1.0));
        Assert.Equal(420 + 39, at.H);
        Assert.Equal(560 - 16, at.Y + at.H);
        // Over the board's lower part.
        Assert.True(at.Y < 176 + 16);
        // Shorter than that: the whole work area, from its top.
        at = SettingsPlacement.For(new Rect(0, 30, 1024, 400), 1.0, Frame(1.0));
        Assert.Equal((30, 400), (at.Y, at.H));
    }

    [Fact]
    public void A_narrow_monitor_limits_the_width()
    {
        var at = SettingsPlacement.For(new Rect(100, 0, 480, 900), 1.0, Frame(1.0));
        Assert.Equal((100, 480), (at.X, at.W));
    }
}
