using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Tack.App.Pinboard.Model;
using Tack.Core.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// Dragging a print out of the board to another app, with WinUI's own drag
/// source, started from the pointer that moved the press past the drag
/// distance. It carries the print's file, as Explorer would, so it can be
/// dropped wherever Windows takes a file, and a note's text too; the file's
/// own picture follows the pointer. The app marks the drag, so the board never
/// tucks under it, and unpins the print if the drop moved its file away.
/// UI thread only.
/// </summary>
sealed class PrintDrag(BoardContext board)
{
    /// <summary>
    /// The effects an Explorer drag offers: Chromium pages that set no drop
    /// effect take copy only when all three are offered; offered copy and move,
    /// they move.
    /// </summary>
    const DataPackageOperation Allowed = DataPackageOperation.Copy | DataPackageOperation.Move | DataPackageOperation.Link;

    /// <summary>Drags <paramref name="item"/> out from <paramref name="point"/>; returns at once.</summary>
    public async void Start(BoardItem item, PointerPoint point)
    {
        var slot = item.View.Slot.Element;
        string? path = null;
        async void Starting(UIElement sender, DragStartingEventArgs e)
        {
            var deferral = e.GetDeferral();
            try
            {
                path = await board.Actions.DragOutAsync(item.Id);
                if (path is null)
                {
                    e.Cancel = true;
                    return;
                }
                e.AllowedOperations = Allowed;
                // An array: WinRT is handed a type it can project, which a collection expression's own type is not.
                IStorageItem[] files = [await StorageFile.GetFileFromPathAsync(path)];
                e.Data.SetStorageItems(files, readOnly: false);
                if (item.Print.Note is { } note)
                {
                    e.Data.SetText(note.Link ?? note.Text);
                }
                if (item.Print.Thumb is { } thumb)
                {
                    await DragPicture.ShowAsync(e.DragUI, thumb, slot.XamlRoot.RasterizationScale);
                }
            }
            catch (Exception ex)
            {
                DevTrace.Write($"drag: {item.Id} cannot be dragged out: {ex.GetType().Name} {ex.Message}");
                e.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        }
        slot.DragStarting += Starting;
        try
        {
            var done = await slot.StartDragAsync(point);
            DevTrace.Write($"drag: {item.Id} dropped with {done}");
        }
        catch (Exception e)
        {
            DevTrace.Write($"drag: {item.Id} did not start: {e.Message}");
        }
        finally
        {
            slot.DragStarting -= Starting;
            board.Actions.DragOutEnded(item.Id, path);
        }
    }
}
