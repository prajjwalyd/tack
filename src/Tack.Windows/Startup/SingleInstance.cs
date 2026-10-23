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
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
            if (createdNew)
            {
                return new SingleInstance(mutex);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // A Tack running elevated made the mutex, and this one may not open
            // it. Tack 0.2 started anyway when it could not create the mutex;
            // this one leaves, deliberately: a second Tack would pin every
            // screenshot twice, and the one already running stays.
            return null;
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
