using System.Net;
using Tack.Core.NetBird;

namespace Tack.Core.Tests.NetBird;

public class NetBirdStatusTests
{
    const string Connected = """
        {
            "peers": { "total": 2, "connected": 0, "details": [
                { "fqdn": "pixel.netbird.cloud", "netbirdIp": "100.90.1.2", "publicKey": "cGl4ZWw=", "status": "Idle" },
                { "fqdn": "jetson.example.org.", "netbirdIp": "100.90.1.3", "publicKey": "amV0c29u", "status": "Connected" }
            ] },
            "netbirdIp": "100.90.53.74/16",
            "fqdn": "my-pc.netbird.cloud",
            "lazyConnectionEnabled": true
        }
        """;

    [Fact]
    public void Reads_this_device_and_its_peers()
    {
        var status = NetBirdStatus.Parse(Connected)!;

        Assert.Equal(IPAddress.Parse("100.90.53.74"), status.Ip);
        Assert.Equal("my-pc.netbird.cloud", status.Fqdn);
        Assert.Equal(2, status.Peers.Count);
        Assert.Equal("jetson.example.org", status.PeerAt(IPAddress.Parse("100.90.1.3"))!.Fqdn);
        Assert.Equal("pixel", status.Peers[0].ShortName);
        Assert.Equal("cGl4ZWw=", status.Peers[0].Key);
    }

    [Fact]
    public void An_unknown_address_is_no_peer() =>
        Assert.Null(NetBirdStatus.Parse(Connected)!.PeerAt(IPAddress.Parse("192.168.1.20")));

    [Theory]
    [InlineData("""{ "netbirdIp": "", "fqdn": "", "peers": { "details": [] } }""")]
    [InlineData("Daemon status: NeedsLogin")]
    [InlineData("""{ "netbirdIp": 3, "fqdn": "a.b" }""")]
    public void Not_connected_is_none(string json) => Assert.Null(NetBirdStatus.Parse(json));

    [Fact]
    public void A_peer_without_an_address_or_key_is_skipped()
    {
        var status = NetBirdStatus.Parse("""
            { "netbirdIp": "100.90.0.1/16", "fqdn": "a.b", "peers": { "details": [
                { "fqdn": "x.b", "netbirdIp": "", "publicKey": "eA==" },
                { "fqdn": "y.b", "netbirdIp": "100.90.0.2", "publicKey": "" }
            ] } }
            """)!;

        Assert.Empty(status.Peers);
    }

    [Theory]
    [InlineData("100.90.53.74", true)]
    [InlineData(" 100.90.53.74 ", true)]
    [InlineData("1", false)]
    [InlineData("0x64.90.53.74", false)]
    [InlineData("100.90.53", false)]
    [InlineData("100.90.53.256", false)]
    [InlineData("fd00::1", false)]
    public void Only_a_plain_ipv4_address_counts(string text, bool valid) =>
        Assert.Equal(valid, NetBirdStatus.ParseIpv4(text) != null);
}
