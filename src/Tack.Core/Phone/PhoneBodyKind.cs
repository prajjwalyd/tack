namespace Tack.Core.Phone;

/// <summary>Which body the phone page shows under its switch: what is needed, or the code and address.</summary>
public enum PhoneBodyKind
{
    /// <summary>NetBird is not installed: what it is, and Get NetBird.</summary>
    GetNetBird,

    /// <summary>The switch is off: who may ask.</summary>
    Off,

    /// <summary>NetBird is not connected on this PC.</summary>
    Disconnected,

    /// <summary>Being served: the QR code and the address with Copy.</summary>
    Share,

    /// <summary>The board could not be served: why.</summary>
    Problem,

    /// <summary>On and waiting to be served.</summary>
    GettingReady,
}
