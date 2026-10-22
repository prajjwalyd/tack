using System.Net;
using Tack.Core.NetBird;
using Tack.Core.Phone;

namespace Tack.Core.Tests.Phone;

public class NetBirdStatesTests
{
    static readonly NetBirdStatus Status = new(IPAddress.Parse("100.90.53.74"), "my-pc.netbird.cloud", []);

    [Fact]
    public void A_status_is_connected_whatever_the_install_check_says()
    {
        Assert.Equal(NetBirdState.Connected, NetBirdStates.Of(Status, installed: true));
        Assert.Equal(NetBirdState.Connected, NetBirdStates.Of(Status, installed: false));
    }

    [Fact]
    public void No_status_is_disconnected_when_installed_and_missing_when_not()
    {
        Assert.Equal(NetBirdState.Disconnected, NetBirdStates.Of(null, installed: true));
        Assert.Equal(NetBirdState.Missing, NetBirdStates.Of(null, installed: false));
    }
}
