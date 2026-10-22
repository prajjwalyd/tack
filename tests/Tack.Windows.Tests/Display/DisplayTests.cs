using Tack.Windows.Display;

namespace Tack.Windows.Tests.Display;

public sealed class DisplayTests
{
    [Fact]
    public void Every_monitor_holds_its_work_area()
    {
        var monitors = Monitors.All();
        Assert.NotEmpty(monitors);
        foreach (var bounds in monitors)
        {
            var monitor = Monitors.At(bounds.X, bounds.Y);
            Assert.Equal(bounds, monitor.Bounds);
            Assert.NotNull(monitor.Bounds.Intersect(monitor.Work));
            Assert.True(monitor.Scale >= 1.0);
        }
    }
}
