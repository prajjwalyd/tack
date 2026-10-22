using System.Net;
using Tack.Core.NetBird;

namespace Tack.Core.Phone;

/// <summary>
/// What one look at NetBird means for the phone server. A server keeps
/// running only while NetBird still puts this PC at the address it listens
/// on; one at another address, or with no status at all (NetBird gone, or the
/// switch off, when NetBird is not asked), stops. With a status and nothing
/// left running, a server starts on the status's address.
/// </summary>
/// <param name="Keep">The running server carries on, and is handed the fresh status.</param>
/// <param name="Stop">The running server stops, and the requests waiting on it are forgotten.</param>
/// <param name="Start">A server starts on the status's address.</param>
public sealed record ServingPlan(bool Keep, bool Stop, bool Start)
{
    /// <summary>The plan for a server on <paramref name="serving"/> (null when none runs) and NetBird's <paramref name="status"/> now.</summary>
    public static ServingPlan For(IPAddress? serving, NetBirdStatus? status)
    {
        var keep = serving is not null && status is not null && serving.Equals(status.Ip);
        return new(keep, serving is not null && !keep, status is not null && !keep);
    }
}
