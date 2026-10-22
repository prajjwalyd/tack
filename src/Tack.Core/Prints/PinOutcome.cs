namespace Tack.Core.Prints;

/// <summary>How a pin went. The board only reports it; the app decides what to save, show and clean up.</summary>
public abstract record PinOutcome
{
    PinOutcome()
    {
    }

    /// <summary>A new print, plus any unkept prints that aged out meanwhile (the fifty-first pushes out the oldest).</summary>
    public sealed record New(Print Print, IReadOnlyList<Print> Aged) : PinOutcome;

    /// <summary>
    /// A restored print already past the history's limits: it was not pinned,
    /// and its file, if Tack owns it, has no use any more.
    /// </summary>
    public sealed record Expired(Print Print, IReadOnlyList<Print> Aged) : PinOutcome;

    /// <summary>
    /// The screenshot file of a capture pinned moments ago, the same pixels:
    /// that print now shows the file, and the capture's own copy at
    /// <paramref name="Capture"/> is redundant.
    /// </summary>
    public sealed record Merged(Print Print, string Capture) : PinOutcome;

    /// <summary>A capture of a screenshot file pinned moments ago, the same pixels; nothing was pinned.</summary>
    public sealed record Duplicate : PinOutcome
    {
        public static Duplicate Instance { get; } = new();
    }

    /// <summary>Already on the board: the same file, or a note with the same text.</summary>
    public sealed record Exists : PinOutcome
    {
        public static Exists Instance { get; } = new();
    }
}
