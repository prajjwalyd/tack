namespace Tack.Core.Phone;

/// <summary>
/// What the phone card and the phone page of Settings show: the switch,
/// NetBird, whether the board is being served and where, what went wrong, the
/// allowed devices and the devices asking. Device names are NetBird's names
/// for them and are only ever shown as text.
/// </summary>
public sealed record PhoneState
{
    /// <summary>The user's switch.</summary>
    public bool On { get; init; }

    public NetBirdState NetBird { get; init; } = NetBirdState.Connected;

    /// <summary>The board is being served on this PC's NetBird address.</summary>
    public bool Serving { get; init; }

    /// <summary>Where a phone opens the board, as "http://my-pc.netbird.cloud:7717"; null while not serving.</summary>
    public string? Address { get; init; }

    /// <summary>The address as a QR code, when there is one.</summary>
    public QrMatrix? Qr { get; init; }

    /// <summary>Why the board could not be served, in words for the person; null when nothing went wrong.</summary>
    public string? Error { get; init; }

    /// <summary>The devices allowed to use the board, in the order they were allowed.</summary>
    public IReadOnlyList<Device> Devices { get; init; } = [];

    /// <summary>The devices waiting for an answer, oldest first.</summary>
    public IReadOnlyList<Asking> Pending { get; init; } = [];

    /// <summary>
    /// What board.json alone tells: the switch and the devices, with NetBird
    /// taken as connected and nothing served or asking. What Settings shows
    /// until something watches NetBird and serves the board.
    /// </summary>
    public static PhoneState Saved(PhoneSettings saved) => new() { On = saved.On, Devices = saved.Devices };
}
