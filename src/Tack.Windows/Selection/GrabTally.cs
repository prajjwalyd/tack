namespace Tack.Windows.Selection;

/// <summary>
/// How many grabs are changing the clipboard, so the snipping listener ignores
/// the changes they cause. Each grab counts until a tail after it ends (a last
/// clipboard event may still be on its way), and only for itself, so an
/// earlier grab's tail never ends a later grab's count.
/// </summary>
sealed class GrabTally(TimeSpan tail)
{
    int _count;

    /// <summary>A grab, or its tail, is running.</summary>
    public bool Any => Volatile.Read(ref _count) > 0;

    public void Begin() => Interlocked.Increment(ref _count);

    /// <summary>Ends one grab once its tail has passed, on a timer: nothing waits.</summary>
    public void EndAfterTail() =>
        _ = Task.Delay(tail).ContinueWith(_ => Interlocked.Decrement(ref _count), TaskScheduler.Default);
}
