using Tack.Core.Prints;

namespace Tack.Core.Pinboard.Input;

/// <summary>
/// The lines of a print's right-click menu, in order. A note's lines speak of
/// its text or link and it has no "Edit" (it would repeat "Open"); only a
/// capture, which lives in Tack's own folder until it is saved somewhere
/// real, offers "Save to Pictures".
/// </summary>
public static class PrintMenuModel
{
    /// <summary>The menu for <paramref name="print"/> as it is now.</summary>
    public static IReadOnlyList<PrintMenuEntry> For(Print print) =>
        For(print.IsNote, print.Note?.Link is not null, print.Origin == Origin.Capture, print.Kept);

    /// <summary>The menu for a print that is a note (with a link or not), a capture, kept or not.</summary>
    public static IReadOnlyList<PrintMenuEntry> For(bool note, bool link, bool capture, bool kept)
    {
        var (copy, open) = (note, link) switch
        {
            (true, true) => ("Copy link", "Open link"),
            (true, false) => ("Copy text", "Open"),
            _ => ("Copy", "Open"),
        };
        var entries = new List<PrintMenuEntry>
        {
            new(copy, PrintMenuAction.Copy),
            new(open, PrintMenuAction.Open),
        };
        if (!note)
        {
            entries.Add(new("Edit", PrintMenuAction.Edit));
        }
        entries.Add(new("Show in Explorer", PrintMenuAction.ShowInExplorer));
        if (capture && !note)
        {
            entries.Add(new("Save to Pictures", PrintMenuAction.SaveToPictures));
        }
        entries.Add(PrintMenuEntry.Separator);
        entries.Add(kept ? new("Stop keeping", PrintMenuAction.StopKeeping) : new("Keep", PrintMenuAction.Keep));
        entries.Add(new("Unpin", PrintMenuAction.Unpin));
        entries.Add(new("Move to Recycle Bin", PrintMenuAction.Recycle));
        return entries;
    }
}
