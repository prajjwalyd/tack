namespace Tack.Windows.Selection;

/// <summary>What became of the user's clipboard after a grab.</summary>
public abstract record ClipboardFate
{
    /// <summary>Never changed by the grab: nothing to put back.</summary>
    public static ClipboardFate Untouched { get; } = new UntouchedFate();

    /// <summary>Put back as it was.</summary>
    public static ClipboardFate Restored { get; } = new RestoredFate();

    /// <summary>Another app wrote to it after the copy: theirs stays.</summary>
    public static ClipboardFate LeftNewer { get; } = new LeftNewerFate();

    ClipboardFate()
    {
    }

    /// <summary>See <see cref="Untouched"/>.</summary>
    public sealed record UntouchedFate : ClipboardFate;

    /// <summary>See <see cref="Restored"/>.</summary>
    public sealed record RestoredFate : ClipboardFate;

    /// <summary>See <see cref="LeftNewer"/>.</summary>
    public sealed record LeftNewerFate : ClipboardFate;

    /// <summary>Not put back, or only in part: the user's next paste will not be what they expect.</summary>
    public sealed record Failed(string Why) : ClipboardFate;
}
