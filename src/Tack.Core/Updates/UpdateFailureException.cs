namespace Tack.Core.Updates;

/// <summary>
/// A check or install that failed in a way Tack itself noticed, already
/// sorted into the kind the person is told. The message is for the trace,
/// in a few words, as "the server answered 404".
/// </summary>
public sealed class UpdateFailureException(UpdateFailure kind, string message) : Exception(message)
{
    /// <summary>What the person is told.</summary>
    public UpdateFailure Kind { get; } = kind;
}
