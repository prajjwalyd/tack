namespace Tack.Windows.Startup;

/// <summary>
/// Lets one Tack run at a time, through a named mutex. Windows lets go of the
/// name when the process ends, however it ends.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    readonly Mutex _mutex;

    SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Claims <paramref name="name"/>, or returns null when another Tack holds it.</summary>
    public static SingleInstance? TryClaim(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
