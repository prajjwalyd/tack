using System.Net;
using Tack.Core.NetBird;
using Tack.Core.Phone;

namespace Tack.Core.Tests.Phone;

public class ServingPlanTests
{
    static readonly IPAddress Here = IPAddress.Parse("100.90.53.74");
    static readonly IPAddress Elsewhere = IPAddress.Parse("100.90.1.2");

    static NetBirdStatus At(IPAddress ip) => new(ip, "my-pc.netbird.cloud", []);

    [Fact]
    public void A_status_with_nothing_running_starts_a_server()
    {
        Assert.Equal(new ServingPlan(Keep: false, Stop: false, Start: true), ServingPlan.For(null, At(Here)));
    }

    [Fact]
    public void A_server_at_the_status_address_keeps_running()
    {
        Assert.Equal(new ServingPlan(Keep: true, Stop: false, Start: false), ServingPlan.For(Here, At(IPAddress.Parse("100.90.53.74"))));
    }

    [Fact]
    public void A_server_at_another_address_stops_and_one_starts_at_the_new_address()
    {
        Assert.Equal(new ServingPlan(Keep: false, Stop: true, Start: true), ServingPlan.For(Here, At(Elsewhere)));
    }

    [Fact]
    public void Without_a_status_a_server_stops_and_none_starts()
    {
        // NetBird disconnected, or the switch is off and NetBird was not asked.
        Assert.Equal(new ServingPlan(Keep: false, Stop: true, Start: false), ServingPlan.For(Here, null));
        Assert.Equal(new ServingPlan(Keep: false, Stop: false, Start: false), ServingPlan.For(null, null));
    }
}
