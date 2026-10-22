using Tack.Windows.Edge;

namespace Tack.Windows.Tests.Edge;

[Collection("EdgeStrips")]
public sealed class EdgeStripsTests
{
    [Fact]
    public void The_strips_start_and_stop_without_a_trace()
    {
        var strips = EdgeStrips.Start(_ => { });
        Assert.False(strips.Enabled);
        strips.Enabled = true;
        Assert.True(strips.Enabled);
        strips.Enabled = false;
        strips.Dispose();
        // Its window class went with it, so the strips can be made again.
        EdgeStrips.Start(_ => { }).Dispose();
    }
}
