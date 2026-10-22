using Tack.Core.NetBird;

namespace Tack.Core.Phone;

/// <summary>What a look at NetBird says about it, in the words the phone card and page use.</summary>
public static class NetBirdStates
{
    /// <summary>
    /// Connected when NetBird gave a status (this PC has an address on the
    /// network); otherwise Disconnected if the client is
    /// <paramref name="installed"/>, and Missing if it is not.
    /// </summary>
    public static NetBirdState Of(NetBirdStatus? status, bool installed) => status switch
    {
        not null => NetBirdState.Connected,
        null when installed => NetBirdState.Disconnected,
        null => NetBirdState.Missing,
    };
}
