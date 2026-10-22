using Tack.Core.Prints;
using Tack.Core.Reveal;
using Tack.Core.Screenshots;
using Tack.Core.Settings;
using Tack.Core.Storage;

namespace Tack.App;

/// <summary>
/// Everything the app's threads share, behind one lock: the board model, the
/// settings, the board on screen, and a little bookkeeping. Hold
/// <see cref="Gate"/> for every member and keep it short: never across a
/// window operation, a decode or the disk. Saving only takes a snapshot under
/// the lock; the saver writes board.json outside it.
/// </summary>
sealed class AppState
{
    readonly Storage _storage;

    /// <summary>board.json could not be read or backed up, so this session must not write over it.</summary>
    readonly bool _holdSaves;

    /// <summary>
    /// Prints saved last session and not on the board (yet): all of them until
    /// the restore is over, then those it could not read this time. Every save
    /// lists them, so none is forgotten.
    /// </summary>
    List<SavedPrint> _restore;

    AppState(Storage storage, Saved saved, Loaded loaded)
    {
        _storage = storage;
        Settings = saved.Settings;
        _restore = saved.Prints;
        _holdSaves = loaded.MustNotSave;
    }

    /// <summary>The lock over everything here.</summary>
    public Lock Gate { get; } = new();

    public Board Board { get; } = new();

    public TackSettings Settings { get; set; }

    public View View { get; } = new();

    /// <summary>The Screenshots folder's files in hand, so a burst of events for one file decodes it once.</summary>
    public FolderEvents Files { get; } = new();

    /// <summary>Reads board.json (see <see cref="Storage.Load"/>); the board starts empty until the restore puts prints back.</summary>
    public static AppState Load(Storage storage)
    {
        var (saved, loaded) = storage.Load();
        return new AppState(storage, saved, loaded);
    }

    /// <summary>Last session's prints, to restore. A copy: they stay in every save until <see cref="FinishRestore"/>.</summary>
    public IReadOnlyList<SavedPrint> ToRestore() => [.. _restore];

    /// <summary>
    /// The restore is over. <paramref name="retry"/> are the prints whose files
    /// could not be read this time (offline, locked): they stay listed in
    /// board.json for the next start rather than being dropped.
    /// </summary>
    public void FinishRestore(List<SavedPrint> retry) => _restore = retry;

    /// <summary>
    /// Has the prints, the prints not restored and the settings written to
    /// board.json shortly, unless that would overwrite a board.json nobody
    /// could back up. Never waits for the disk.
    /// </summary>
    public void Save()
    {
        if (!_holdSaves)
        {
            _storage.Queue(Snapshot.Of(Board.Prints, _restore, Settings));
        }
    }
}
