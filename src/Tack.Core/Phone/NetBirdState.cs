namespace Tack.Core.Phone;

/// <summary>NetBird on this PC, as the phone card and page describe it.</summary>
public enum NetBirdState
{
    /// <summary>Not installed.</summary>
    Missing,

    /// <summary>Installed, with no address on the NetBird network.</summary>
    Disconnected,
    Connected,
}
