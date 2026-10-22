namespace Tack.Core.Captures;

/// <summary>
/// Tells a fresh capture from the same pixels arriving again moments later:
/// one copy can change the clipboard several times (OLE sets it, then flushes it).
/// </summary>
public sealed class RepeatFilter
{
    /// <summary>The same pixels again this soon are the same capture.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    (ulong Hash, TimeSpan At)? _last;

    /// <summary>
    /// Records a capture with this pixel hash, read at <paramref name="now"/> on the
    /// monotonic clock, and says whether it repeats the previous one. Each read
    /// restarts the window.
    /// </summary>
    public bool IsRepeat(ulong hash, TimeSpan now)
    {
        var repeat = _last is { } last && last.Hash == hash && (now > last.At ? now - last.At : TimeSpan.Zero) < Window;
        _last = (hash, now);
        return repeat;
    }
}
