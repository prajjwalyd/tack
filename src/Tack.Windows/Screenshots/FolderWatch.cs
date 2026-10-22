using Tack.Core.Diagnostics;

namespace Tack.Windows.Screenshots;

/// <summary>
/// Reports what happens to the files in one folder (not its subfolders) until
/// disposed. It sleeps in the kernel until something changes: no timer, no
/// polling. Deciding what a change means for the board is the caller's job.
/// </summary>
/// <remarks>
/// Events come on thread-pool threads, one at a time; a handler should only
/// hand the work on, and must not throw. When the watch fails (its buffer
/// overflowed, say) it starts again; more changes than the buffer holds are
/// lost, as the callers only act on single files.
/// </remarks>
public sealed class FolderWatch : IDisposable
{
    /// <summary>What the kernel holds between reads; network shares take at most 64 KB.</summary>
    const int BufferSize = 64 * 1024;

    const NotifyFilters Filter =
        NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime;

    readonly Lock _lock = new();
    FileSystemWatcher? _watcher;

    /// <summary>Starts watching <paramref name="folder"/>, which must exist.</summary>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public FolderWatch(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"{folder} does not exist.");
        }
        Folder = folder;
        _watcher = Start();
    }

    public string Folder { get; }

    /// <summary>A file changed: what happened, and its full path.</summary>
    public event Action<FolderChange, string>? Changed;

    /// <summary>The folder went away (deleted, or its drive gone) and the watch ended.</summary>
    public event Action? Ended;

    /// <summary>The watch failed and started again.</summary>
    internal event Action? Restarted;

    FileSystemWatcher Start()
    {
        var watcher = new FileSystemWatcher(Folder)
        {
            InternalBufferSize = BufferSize,
            IncludeSubdirectories = false,
            NotifyFilter = Filter,
        };
        watcher.Created += (sender, e) => Report(sender, FolderChange.Arrived, e.FullPath);
        watcher.Deleted += (sender, e) => Report(sender, FolderChange.Gone, e.FullPath);
        watcher.Changed += (sender, e) => Report(sender, FolderChange.Changed, e.FullPath);
        watcher.Renamed += OnRenamed;
        watcher.Error += OnError;
        try
        {
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
        return watcher;
    }

    void OnRenamed(object sender, RenamedEventArgs e)
    {
        // A rename whose other half fell into another read has an empty name.
        if (!string.IsNullOrEmpty(e.OldName))
        {
            Report(sender, FolderChange.Gone, e.OldFullPath);
        }
        if (!string.IsNullOrEmpty(e.Name))
        {
            Report(sender, FolderChange.Arrived, e.FullPath);
        }
    }

    /// <summary>Passes a change on, unless it comes from a watcher already replaced or stopped.</summary>
    void Report(object sender, FolderChange change, string path)
    {
        if (ReferenceEquals(sender, Volatile.Read(ref _watcher)))
        {
            Changed?.Invoke(change, path);
        }
    }

    void OnError(object sender, ErrorEventArgs e)
    {
        DevTrace.Write($"folder watch: {Folder} failed: {e.GetException().Message}");
        // Restarted from another thread, so the failed watcher is not disposed
        // inside its own callback.
        ThreadPool.QueueUserWorkItem(_ => Restart(sender));
    }

    void Restart(object failed)
    {
        bool ended;
        lock (_lock)
        {
            if (!ReferenceEquals(failed, _watcher))
            {
                return;
            }
            _watcher.Dispose();
            _watcher = null;
            try
            {
                _watcher = Start();
                ended = false;
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
            {
                DevTrace.Write($"folder watch: stopped watching {Folder}: {e.Message}");
                ended = true;
            }
        }
        (ended ? Ended : Restarted)?.Invoke();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }
}
