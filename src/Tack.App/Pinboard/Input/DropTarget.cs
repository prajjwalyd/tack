using Microsoft.UI.Xaml;
using Tack.Core.Diagnostics;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using DragEventArgs = Microsoft.UI.Xaml.DragEventArgs;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// Dropping onto the board to pin: PNG and JPEG files, a link or text
/// (<see cref="DropRules"/>), with the "Drop to pin" cue and the copy cursor
/// while something pinnable is carried over it. Only while the board is down,
/// and never a print being dragged out of it. A drop that cannot be pinned
/// says why under the board.
/// </summary>
/// <remarks>
/// The window lets the pointer through everywhere but over the board; the
/// edge poller makes it hit-testable while a drag (from another app, with
/// the button pressed outside the board) is over the board rect, which is
/// when Windows' drag loop finds the window and these events come.
/// </remarks>
sealed class DropTarget : BoardPart
{
    readonly DropCue _cue;

    public DropTarget(BoardContext board) : base(board)
    {
        _cue = new DropCue(board);
        // The board's own background (set by the scroller) makes the felt between prints a target too.
        var area = board.Layers.Board;
        area.AllowDrop = true;
        area.DragEnter += Drag;
        area.DragOver += Drag;
        area.DragLeave += Leave;
        area.Drop += Drop;
    }

    bool Takes => Board.State.Revealed && Board.State.DraggingId is null;

    public override void Tucking() => _cue.Show(false);

    public override void Tucked() => _cue.Settle();

    public override void ThemeChanged() => _cue.ApplyTheme();

    public override void DragStarted(string id, Model.BoardItem? item) => _cue.Show(false);

    void Drag(object sender, DragEventArgs e)
    {
        var ok = Takes && DropReader.Pinnable(e.DataView);
        e.AcceptedOperation = ok ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.IsCaptionVisible = false;
        e.Handled = true;
        _cue.Show(ok);
    }

    /// <summary>
    /// Leaving one of the board's parts for another is no leave; only leaving
    /// the board itself (off it, or the drag given up over it) is.
    /// </summary>
    void Leave(object sender, DragEventArgs e)
    {
        var area = Board.Layers.Board;
        var at = e.GetPosition(area);
        var inside = at.X > 0 && at.X < Board.State.BoardWidth && at.Y > BoardMetrics.TopHidden && at.Y < BoardMetrics.ElementHeight;
        if (!inside || ReferenceEquals(e.OriginalSource, area))
        {
            _cue.Show(false);
        }
    }

    async void Drop(object sender, DragEventArgs e)
    {
        _cue.Show(false);
        e.Handled = true;
        if (!Takes)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        var deferral = e.GetDeferral();
        try
        {
            var (plan, files) = await DropReader.ReadAsync(e.DataView);
            // Read while the drop holds its data; the pictures' own files outlast it.
            deferral.Complete();
            deferral = null;
            await PinAsync(plan, files);
        }
        catch (Exception ex)
        {
            DevTrace.Write($"board: could not read a drop: {ex.Message}");
            Say(Notices.Failed);
        }
        finally
        {
            deferral?.Complete();
        }
    }

    async Task PinAsync(DropPlan plan, Dictionary<DroppedFile, StorageFile?> files)
    {
        switch (plan.Outcome)
        {
            case DropOutcome.Text:
                Board.Actions.PinText(plan.Text!);
                break;
            case DropOutcome.NotPinnable:
                Say(Notices.NotPinnable);
                break;
            case DropOutcome.BadImage:
                Say(Notices.BadImage);
                break;
            case DropOutcome.Images:
                foreach (var image in plan.Images)
                {
                    if (await DropReader.BytesAsync(files[image]) is { } bytes)
                    {
                        Board.Actions.PinImage(string.IsNullOrEmpty(image.Name) ? "Image.png" : image.Name, bytes);
                    }
                    else
                    {
                        Say(Notices.BadImage);
                    }
                }
                break;
        }
    }

    /// <summary>A notice from the board itself, under the board and aloud.</summary>
    void Say(string text) => Board.Parts.NoticePill.Notice(text);
}
