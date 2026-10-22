namespace Tack.Core.Phone.Serving;

/// <summary>
/// How much a device may pin: at most <see cref="PinsPerMinute"/> per device
/// key in any minute, and at most <see cref="MaxPinning"/> pins being handled
/// at once across all devices. Safe on any thread.
/// </summary>
public sealed class PinLimits
{
    static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);

    readonly TimeProvider _time;
    readonly Lock _gate = new();

    /// <summary>When each device's recent pins came, oldest first, as the clock's timestamps.</summary>
    readonly Dictionary<string, Queue<long>> _recent = [];

    int _pinning;

    public PinLimits(TimeProvider time) => _time = time;

    /// <summary>Pins being handled at once; another gets 503.</summary>
    public const int MaxPinning = 2;

    /// <summary>Pins per device in any 60 seconds; another gets 429.</summary>
    public const int PinsPerMinute = 30;

    /// <summary>Counts a pin from <paramref name="key"/>; false when it is over the per-minute limit (and then it is not counted).</summary>
    public bool MayPin(string key)
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (!_recent.TryGetValue(key, out var recent))
            {
                _recent[key] = recent = new Queue<long>();
            }
            while (recent.Count > 0 && _time.GetElapsedTime(recent.Peek(), now) >= Minute)
            {
                recent.Dequeue();
            }
            if (recent.Count >= PinsPerMinute)
            {
                return false;
            }
            recent.Enqueue(now);
            return true;
        }
    }

    /// <summary>A turn to pin, held until disposed; null when <see cref="MaxPinning"/> are under way.</summary>
    public IDisposable? TryTurn()
    {
        if (Interlocked.Increment(ref _pinning) > MaxPinning)
        {
            Interlocked.Decrement(ref _pinning);
            return null;
        }
        return new PinTurn(() => Interlocked.Decrement(ref _pinning));
    }
}
