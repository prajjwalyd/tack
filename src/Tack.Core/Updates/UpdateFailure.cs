namespace Tack.Core.Updates;

/// <summary>
/// The kinds of failure the person is told apart, each with a fixed message
/// (<see cref="UpdateFailures.Words"/>): what went wrong in detail goes to the
/// trace only.
/// </summary>
public enum UpdateFailure
{
    /// <summary>The update server could not be reached, or did not answer in time.</summary>
    Network,

    /// <summary>The server answered, but not with a usable update.</summary>
    Server,

    /// <summary>The download is not what Tack's key signed for the announced version.</summary>
    Signature,

    /// <summary>Something on this PC: the file could not be written or the installer not started.</summary>
    Local,

    /// <summary>Anything else.</summary>
    Other,
}
