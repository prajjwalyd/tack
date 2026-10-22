namespace Tack.Core.Reveal;

/// <summary>
/// The user's "Speed" setting: how quickly the board moves and how long it
/// waits, as one choice. <see cref="RevealSpeedTimes"/> turns it into times.
/// Saved as "instant", "normal" or "slow".
/// </summary>
public enum RevealSpeed
{
    Normal,

    /// <summary>Almost at once: the pointer need hardly rest.</summary>
    Instant,

    /// <summary>Long enough that passing through the edge, to a title bar or a tab, does not count.</summary>
    Slow,
}
