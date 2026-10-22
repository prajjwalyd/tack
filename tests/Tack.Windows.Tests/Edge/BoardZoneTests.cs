using Tack.Core.Geometry;
using Tack.Core.Reveal;
using Tack.Windows.Edge;

namespace Tack.Windows.Tests.Edge;

public sealed class BoardZoneTests
{
    /// <summary>A board down on a 200% monitor whose work area starts 40 px below its top.</summary>
    static View Down() => new()
    {
        Shown = true,
        WindowPos = (0, 40),
        WindowSize = (3840, 2000),
        MonitorTop = 0,
        Scale = 2.0,
        BoardRect = new Rect(1000, 20, 1800, 300),
    };

    [Fact]
    public void The_leave_zone_keeps_a_margin_and_reaches_up_to_the_monitor_top()
    {
        var zone = BoardZone.Leave(Down());
        // 10 logical px at 200% around the board at (1000, 60) on screen.
        Assert.Equal(new Rect(980, 0, 1840, 380), zone);
        Assert.Null(BoardZone.Leave(new View()));
    }

    [Fact]
    public void Fresh_from_the_edge_the_whole_top_of_the_window_counts()
    {
        // From the monitor's top through the board area (176 logical px).
        Assert.Equal(new Rect(0, 0, 3840, 40 + 352), BoardZone.FreshStrip(Down()));
    }

    [Theory]
    [InlineData(RevealReason.Edge, false, false, true)]
    [InlineData(RevealReason.Edge, true, false, true)]
    [InlineData(RevealReason.New, false, false, true)]
    [InlineData(RevealReason.New, true, false, false)]
    [InlineData(RevealReason.Hotkey, false, false, false)]
    [InlineData(RevealReason.Tray, false, true, true)]
    [InlineData(RevealReason.Settings, true, true, false)]
    public void A_full_screen_app_tucks_the_edges_and_new_captures_board_and_beats_the_settings_hold(
        RevealReason reason, bool snipping, bool settingsHeld, bool tucks)
    {
        var view = Down();
        view.Reason = reason;
        view.Snipping = snipping;
        view.SettingsHeld = settingsHeld;
        Assert.False(BoardZone.FullScreenTucks(view));
        view.Fullscreen = true;
        Assert.Equal(tucks, BoardZone.FullScreenTucks(view));
    }
}
