namespace Tack.Windows.Selection;

/// <summary>
/// Waits for the modifiers to be released and to stay released for
/// <see cref="Settle"/>, giving up after <see cref="Patience"/>. Fed one
/// reading at a time, with times on the monotonic clock, so it can be tested
/// without a keyboard.
/// </summary>
sealed class ReleaseWait(TimeSpan started)
{
    /// <summary>How long to wait for the shortcut's keys to be let go.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(1500);

    /// <summary>The keys must stay up this long before the copy keys go out.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(30);

    TimeSpan? _upSince;

    /// <param name="held">Any modifier is down at <paramref name="now"/>.</param>
    public Release Step(bool held, TimeSpan now)
    {
        if (held)
        {
            _upSince = null;
            return now - started >= Patience ? Release.TimedOut : Release.Wait;
        }
        _upSince ??= now;
        return now - _upSince >= Settle ? Release.Ready : Release.Wait;
    }
}
