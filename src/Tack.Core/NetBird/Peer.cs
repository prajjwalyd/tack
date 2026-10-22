using System.Net;

namespace Tack.Core.NetBird;

/// <summary>A peer NetBird's access policies let this device see.</summary>
/// <param name="Key">Its WireGuard public key: who it is. An address can move to another peer; the key cannot.</param>
public sealed record Peer(IPAddress Ip, string Fqdn, string Key)
{
    /// <summary>The first label of the name, "pixel" for "pixel.netbird.cloud": how the board names a device.</summary>
    public string ShortName => Fqdn.Split('.')[0];
}
