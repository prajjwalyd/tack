using Tack.Core.Reveal;
using Tack.Core.Settings;
using Tack.Windows.Edge;

namespace Tack.Windows.Tests.Edge;

// Its strips share a window class with EdgeStripsTests.
[Collection("EdgeStrips")]
public sealed class EdgePollerTests
{
    [Fact]
    public void A_tucked_board_with_edge_reveal_off_asks_for_nothing_and_stops_when_told()
    {
        var gate = new Lock();
        var settings = new TackSettings { EdgeReveal = false };
        var asked = 0;
        var poller = new EdgePoller(gate, new View(), () => settings, () => false);
        poller.RevealRequested += () => asked++;
        poller.TuckRequested += () => asked++;
        poller.ClickThroughChanged += _ => asked++;
        poller.Start();
        poller.Wake();
        poller.Dispose();
        // Told again after it stopped: nobody listens, and nothing breaks.
        poller.Wake();
        poller.Dispose();
        Assert.Equal(0, asked);
    }
}
