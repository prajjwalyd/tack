namespace Tack.Core.Phone;

/// <summary>The dot before the phone card's status line: whether it is on, wants attention or needs something, at a glance.</summary>
public enum PhoneTone
{
    /// <summary>No dot.</summary>
    None,

    /// <summary>Serving: the success colour.</summary>
    Ok,

    /// <summary>A device is asking: the accent colour.</summary>
    Attention,

    /// <summary>Something is missing or went wrong: the caution colour.</summary>
    Warn,
}
