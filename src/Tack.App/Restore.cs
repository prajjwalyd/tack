using Tack.Core.Diagnostics;
using Tack.Core.Notes;
using Tack.Core.Prints;
using Tack.Core.Storage;
using Tack.Windows.Imaging;

namespace Tack.App;

/// <summary>
/// Puts last session's prints and notes back at startup, as they were (kept or
/// not, in their old order), then saves board.json once. Until then every save
/// still lists the prints not put back yet, so a quit or crash halfway forgets
/// none of them.
/// </summary>
sealed class Restore(AppState state, Prints prints, string notesFolder)
{
    /// <summary>Starts the restore in the background, then checks the history once for anything that grew too old meanwhile.</summary>
    public void Start() => Background.Run("restoring the board", RunAsync);

    async Task RunAsync()
    {
        IReadOnlyList<SavedPrint> saved;
        lock (state.Gate)
        {
            saved = state.ToRestore();
        }
        if (saved.Count == 0)
        {
            prints.AgeOut();
            return;
        }
        var retry = new List<SavedPrint>();
        var (gone, invalid) = (0, 0);
        foreach (var print in saved)
        {
            switch (RestoreCheck.Of(print, notesFolder))
            {
                case Restoring.Gone:
                    gone++;
                    continue;
                case Restoring.Invalid:
                    invalid++;
                    continue;
            }
            var arrival = new Arrival.Restored(print.PinnedAt, print.KeptAt);
            try
            {
                if (print.Kind == Kind.Note)
                {
                    prints.PinNote(print.Path, NoteFiles.Read(print.Path), arrival);
                }
                else
                {
                    prints.Pin(print.Path, await Thumbs.MakeAsync(print.Path).ConfigureAwait(false), print.Origin, arrival);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                DevTrace.Write($"restore: cannot read {Path.GetFileName(print.Path)} now, keeping it for next time: {e.Message}");
                retry.Add(print);
            }
        }
        DevTrace.Write($"restore: {saved.Count - gone - invalid - retry.Count} print(s) back, {gone} gone, {invalid} not Tack's, {retry.Count} for next time");
        lock (state.Gate)
        {
            state.FinishRestore(retry);
            state.Save();
        }
        prints.AgeOut();
    }
}
