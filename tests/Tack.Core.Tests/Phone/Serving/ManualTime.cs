namespace Tack.Core.Tests.Phone.Serving;

/// <summary>A clock that moves only when a test moves it.</summary>
sealed class ManualTime : TimeProvider
{
    long _ticks = TimeSpan.FromHours(1).Ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
