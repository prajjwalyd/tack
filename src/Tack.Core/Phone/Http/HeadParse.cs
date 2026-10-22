namespace Tack.Core.Phone.Http;

/// <summary>
/// What the bytes of a request so far amount to: a whole head (and how many
/// bytes it took), not a whole head yet, or one that is refused.
/// </summary>
public sealed class HeadParse
{
    /// <summary>No blank line yet, and nothing wrong so far: read more.</summary>
    public static readonly HeadParse Partial = new(null, 0, null);

    HeadParse(RequestHead? head, int length, HeadError? error)
    {
        (Head, Length, Error) = (head, length, error);
    }

    /// <summary>The head, when it is complete.</summary>
    public RequestHead? Head { get; }

    /// <summary>The bytes the head took, its blank line included; what follows is the body's start.</summary>
    public int Length { get; }

    /// <summary>Why the head is refused, when it is.</summary>
    public HeadError? Error { get; }

    public bool IsPartial => Head == null && Error == null;

    public static HeadParse Complete(RequestHead head, int length) => new(head, length, null);

    public static HeadParse Failed(HeadError error) => new(null, 0, error);
}
