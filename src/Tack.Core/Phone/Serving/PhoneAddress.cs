using Tack.Core.NetBird;

namespace Tack.Core.Phone.Serving;

/// <summary>Where the phone board is served: this PC's NetBird address, on one port.</summary>
public static class PhoneAddress
{
    /// <summary>The port the board is served on, on the NetBird address only.</summary>
    public const int Port = 7717;

    /// <summary>What a phone opens: "http://my-pc.netbird.cloud:7717".</summary>
    public static string Of(NetBirdStatus status) => $"http://{status.Fqdn}:{Port}";

    /// <summary>This PC's short name on NetBird: "my-pc" for "my-pc.netbird.cloud".</summary>
    public static string PcName(NetBirdStatus status) => status.Fqdn.Split('.')[0];
}
