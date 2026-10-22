using Tack.Core.Captures;
using Tack.Core.Prints;

namespace Tack.App;

/// <summary>
/// Tack's own files (captures and notes) once their prints leave the board:
/// sent to the Recycle Bin, never deleted outright, on the thread pool since
/// the shell can take a moment. A capture still being written goes once it is
/// written. Screenshot files are the user's and are never touched here.
/// </summary>
sealed class OwnedFiles(Storage storage, PendingSaves saves)
{
    /// <summary>Recycles the files of captures and notes that left the board; screenshot files stay.</summary>
    public void DiscardOf(IEnumerable<Print> prints) => Discard(prints.Where(p => p.Owned).Select(p => p.Path));

    /// <summary>Recycles capture and note files Tack no longer needs, if they lie in Tack's own folders.</summary>
    public void Discard(IEnumerable<string> paths)
    {
        var now = new List<string>();
        foreach (var path in paths)
        {
            if (saves.IsSaving(path))
            {
                saves.WhenSaved(path, written => Discard([written]));
            }
            else
            {
                now.Add(path);
            }
        }
        if (now.Count == 0)
        {
            return;
        }
        Background.Run("recycling Tack's own files", () =>
        {
            foreach (var path in now)
            {
                Tack.Core.Captures.Discard.Owned(path, storage.OwnFolders, storage.Bin);
            }
        });
    }
}
