namespace Tack.Core.Pinboard.Motion;

/// <summary>What a motion shows outside its active time.</summary>
public enum Fill
{
    /// <summary>The layer's own values before the delay and after the end.</summary>
    None,

    /// <summary>Holds the last frame after the end.</summary>
    Forwards,

    /// <summary>Shows the first frame during the delay.</summary>
    Backwards,

    Both,
}
