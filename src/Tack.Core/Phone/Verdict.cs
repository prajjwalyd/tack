namespace Tack.Core.Phone;

/// <summary>What a device asking to use the board gets.</summary>
public abstract record Verdict
{
    Verdict()
    {
    }

    /// <summary>It was allowed: the board is open to it.</summary>
    public sealed record Allowed : Verdict
    {
        public static Allowed Instance { get; } = new();
    }

    /// <summary>
    /// It waits for the user, with the code to compare. <paramref name="Asked"/> is
    /// true when this request put the question to the user, so the PC should show it.
    /// </summary>
    public sealed record Waiting(bool Asked, string Code) : Verdict;

    /// <summary>The user turned it down.</summary>
    public sealed record Refused : Verdict
    {
        public static Refused Instance { get; } = new();
    }

    /// <summary>Too many devices are already waiting.</summary>
    public sealed record Busy : Verdict
    {
        public static Busy Instance { get; } = new();
    }
}
