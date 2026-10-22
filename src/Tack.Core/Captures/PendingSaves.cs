using Tack.Core.Files;

namespace Tack.Core.Captures;

/// <summary>
/// Captures whose file is still being written. A capture is pinned under its
/// reserved name while its PNG is written in the background; anything that
/// needs the file itself (recycling it, copying it, opening it) waits for the
/// write through <see cref="WhenSaved"/> or <see cref="WaitAsync"/>. Safe on
/// any thread.
/// </summary>
public sealed class PendingSaves
{
    /// <summary>The longest anything waits for a capture's file to be written.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    readonly Lock _lock = new();
    readonly Dictionary<string, Pending> _saving = [];

    /// <summary>The file at <paramref name="path"/> is about to be written.</summary>
    public void Begin(string path)
    {
        lock (_lock)
        {
            _saving[PathRules.Key(path)] = new Pending();
        }
    }

    /// <summary>
    /// The file is written (or its write failed): runs what was waiting for
    /// it, on the calling thread, and lets <see cref="WaitAsync"/> return.
    /// </summary>
    public void Finish(string path)
    {
        Pending? pending;
        lock (_lock)
        {
            if (_saving.Remove(PathRules.Key(path), out pending))
            {
                pending.Done.TrySetResult();
            }
        }
        foreach (var then in pending?.Then ?? [])
        {
            then(path);
        }
    }

    /// <summary>The file is a capture still being written.</summary>
    public bool IsSaving(string path)
    {
        lock (_lock)
        {
            return _saving.ContainsKey(PathRules.Key(path));
        }
    }

    /// <summary>
    /// Runs <paramref name="then"/> with the path once the file is written:
    /// straight away for a file that is not a capture still being saved, else
    /// on the thread that finishes the save. Never blocks.
    /// </summary>
    public void WhenSaved(string path, Action<string> then)
    {
        lock (_lock)
        {
            if (_saving.TryGetValue(PathRules.Key(path), out var pending))
            {
                pending.Then.Add(then);
                return;
            }
        }
        then(path);
    }

    /// <summary>Finishes once the file is written, if it is a capture still being saved, or after <paramref name="patience"/>.</summary>
    public async Task WaitAsync(string path, TimeSpan patience)
    {
        Task done;
        lock (_lock)
        {
            if (!_saving.TryGetValue(PathRules.Key(path), out var pending))
            {
                return;
            }
            done = pending.Done.Task;
        }
        try
        {
            await done.WaitAsync(patience).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Not written in time: the caller goes ahead with what is there.
        }
    }

    sealed class Pending
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<Action<string>> Then { get; } = [];
    }
}
