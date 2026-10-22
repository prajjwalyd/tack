namespace Tack.Core.Prints;

/// <summary>How a print comes to the board.</summary>
public abstract record Arrival
{
    Arrival()
    {
    }

    /// <summary>
    /// A screenshot just taken, reaching the board at <paramref name="At"/> on the
    /// monotonic clock. It is pinned now, and may be the other half of an
    /// auto-saved screenshot pinned moments ago.
    /// </summary>
    public sealed record Live(TimeSpan At) : Arrival;

    /// <summary>A print from last session, put back at startup as it was.</summary>
    public sealed record Restored(long PinnedAt, long? KeptAt) : Arrival;
}
