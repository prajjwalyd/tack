using Tack.Core.Diagnostics;

namespace Tack.Core.Storage;

/// <summary>
/// Writes board.json off the app's lock, on a thread of its own. It writes the
/// latest snapshot it has, then rests a moment, so a burst of changes (Clear
/// unpinning fifty prints) costs a few writes. A failed write is tried again a
/// little later unless a newer snapshot replaced it, whatever made it fail: a
/// save that throws must not end the app. The thread sleeps until a
/// snapshot comes: nothing polls.
/// </summary>
public sealed class Saver
{
    /// <summary>
    /// How long a failed write waits before it is tried again: long enough not to
    /// spin while the disk is full, short enough to outlast a scanner holding the file.
    /// </summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    readonly string _file;
    readonly Action<string, Snapshot> _write;
    readonly object _gate = new();
    readonly object _writing = new();
    Snapshot? _next;
    int _writes;

    Saver(string file, Action<string, Snapshot> write)
    {
        _file = file;
        _write = write;
    }

    /// <summary>How many writes were tried, for the tests.</summary>
    internal int Writes => Volatile.Read(ref _writes);

    /// <summary>Starts the thread that writes <paramref name="file"/>, at most once per <paramref name="gap"/>.</summary>
    public static Saver Start(string file, TimeSpan gap)
    {
        var saver = new Saver(file, BoardFile.Write);
        new Thread(() => saver.Run(gap)) { IsBackground = true, Name = "tack-save-board" }.Start();
        return saver;
    }

    /// <summary>A saver with no thread, for the tests: only <see cref="Flush"/> writes, through <paramref name="write"/> if given.</summary>
    internal static Saver Unthreaded(string file, Action<string, Snapshot>? write = null) => new(file, write ?? BoardFile.Write);

    /// <summary>Has <paramref name="snapshot"/> written soon, in place of any older one still waiting. Never waits for the disk.</summary>
    public void Queue(Snapshot snapshot)
    {
        lock (_gate)
        {
            _next = snapshot;
            Monitor.Pulse(_gate);
        }
    }

    /// <summary>Writes the snapshot still waiting, if any, before returning; waits for a write under way first. For Quit.</summary>
    public void Flush() => WriteWaiting();

    internal bool HasWaiting
    {
        get
        {
            lock (_gate)
            {
                return _next != null;
            }
        }
    }

    void Run(TimeSpan gap)
    {
        while (true)
        {
            lock (_gate)
            {
                while (_next is null)
                {
                    Monitor.Wait(_gate);
                }
            }
            Thread.Sleep(WriteWaiting() ? gap : Retry);
        }
    }

    /// <summary>Writes the waiting snapshot. False if that failed; it then waits again, unless something newer took its place.</summary>
    bool WriteWaiting()
    {
        lock (_writing)
        {
            Snapshot? snapshot;
            lock (_gate)
            {
                (snapshot, _next) = (_next, null);
            }
            if (snapshot is null)
            {
                return true;
            }
            Interlocked.Increment(ref _writes);
            try
            {
                _write(_file, snapshot);
                return true;
            }
            catch (Exception e)
            {
                DevTrace.Write($"cannot save board.json, trying again shortly: {e.GetType().Name}: {e.Message}");
                lock (_gate)
                {
                    _next ??= snapshot;
                }
                return false;
            }
        }
    }
}
