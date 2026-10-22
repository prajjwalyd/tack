using Tack.Core.NetBird;

namespace Tack.Core.Phone.Serving;

/// <summary>A connection's peer as NetBird lists it, with this PC's status from the same look.</summary>
/// <param name="Me">This PC on NetBird: its address and name, for the Host check and the page's words.</param>
/// <param name="Peer">The device at the connection's address.</param>
public sealed record PeerMatch(NetBirdStatus Me, Peer Peer);
