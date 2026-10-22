using Tack.Windows.Host;

namespace Tack.Windows.Tests.Host;

[Collection("HostWindow")]
public sealed class HostWindowTests
{
    [Fact]
    public void Tack_has_one_host_window()
    {
        using var host = new HostWindow();
        Assert.NotEqual(0, host.Handle);
        Assert.Throws<InvalidOperationException>(() => new HostWindow());
    }

    [Fact]
    public void A_new_host_window_can_follow_a_closed_one()
    {
        new HostWindow().Dispose();
        using var host = new HostWindow();
        Assert.NotEqual(0, host.Handle);
    }
}
