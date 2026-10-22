using Tack.Core.Captures;
using Tack.Core.Diagnostics;
using Tack.Core.Files;
using Tack.Core.Notes;
using Tack.Core.Prints;
using Tack.Core.Storage;
using Tack.Windows.Files;

namespace Tack.App;

/// <summary>
/// Tack's files on disk: board.json, read once at startup and written by the
/// saver's own thread a moment after each change, and the Captures and Notes
/// folders, whose leftovers the startup sweep sends to the Recycle Bin.
/// </summary>
sealed class Storage
{
    /// <summary>
    /// The least time between two writes of board.json: a burst of changes
    /// (Clear, a restore) is written as its latest state, a few times at most.
    /// </summary>
    static readonly TimeSpan s_saveGap = TimeSpan.FromMilliseconds(250);

    readonly Saver _saver;

    public Storage(TackPaths paths)
    {
        Paths = paths;
        _saver = Saver.Start(paths.BoardFile, s_saveGap);
    }

    public TackPaths Paths { get; }

    /// <summary>Where Tack's own files go when it is done with them; never a hard delete.</summary>
    public IRecycler Bin { get; } = new RecycleBin();

    /// <summary>Tack's own folders, the only ones it ever recycles files from.</summary>
    public IReadOnlyList<string> OwnFolders => [Paths.Captures, Paths.Notes];

    /// <summary>
    /// Reads board.json and lets the history's old prints go. If the file was
    /// understood, leftover captures and notes go to the Recycle Bin in the
    /// background; otherwise none are touched, since any of them might still
    /// be on the board.
    /// </summary>
    public (Saved Saved, Loaded Loaded) Load()
    {
        var now = History.NowMs();
        var file = Paths.BoardFile;
        // Before anything can save over it: when board.json last saved.
        DateTime? boardSaved = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : null;
        var (saved, loaded) = BoardFile.Load(file, now);
        saved.AgeOut(now);
        switch (loaded)
        {
            case Loaded.Ok when MentionedPaths.In(file) is { } keep:
                keep.AddRange(saved.Prints.Select(p => p.Path));
                SweepInBackground(keep, Sweep.Cutoff(DateTime.UtcNow, boardSaved));
                break;
            case Loaded.Ok:
                DevTrace.Write("board.json or a backup of it cannot be read; leftover files are kept");
                break;
            case Loaded.Unreadable { Backup: { } backup } unreadable:
                DevTrace.Write($"{file} was not used ({unreadable.Reason}); a copy is at {backup}. Leftover captures are kept.");
                break;
            case Loaded.Unreadable unreadable:
                DevTrace.Write($"{file} was not used ({unreadable.Reason}) and could not be copied; it will not be saved over");
                break;
        }
        return (saved, loaded);
    }

    /// <summary>Has <paramref name="snapshot"/> written shortly, off the app's lock. Never waits for the disk.</summary>
    public void Queue(Snapshot snapshot) => _saver.Queue(snapshot);

    /// <summary>Writes the last save still waiting before returning: for Quit, so nothing changed in the last moments is lost.</summary>
    public void Flush() => _saver.Flush();

    /// <summary>
    /// Recycles leftover captures and notes off the startup path: the shell can
    /// take a moment. <paramref name="keep"/> is every path board.json or a
    /// backup of it mentions; only files last modified before
    /// <paramref name="cutoff"/> go.
    /// </summary>
    void SweepInBackground(List<string> keep, DateTime cutoff) => Background.Run("the leftover sweep", () =>
    {
        if (SweepOffForDevelopment())
        {
            return;
        }
        var captures = Sweep.Captures(Paths.Captures, keep, cutoff, Bin);
        var notes = NoteFiles.Sweep(Paths.Notes, keep, cutoff, Bin);
        DevTrace.Write($"sweep: {captures.Count} leftover capture(s) and {notes.Count} note(s) moved to the Recycle Bin");
    });

    /// <summary>
    /// Development builds: TACK_DEBUG_NO_SWEEP=1 leaves leftovers alone, for runs
    /// on a throwaway copy of the data (the board-picture script) whose sweep
    /// would otherwise fill the real Recycle Bin.
    /// </summary>
    static bool SweepOffForDevelopment()
    {
#if DEBUG
        return Environment.GetEnvironmentVariable("TACK_DEBUG_NO_SWEEP") == "1";
#else
        return false;
#endif
    }
}
