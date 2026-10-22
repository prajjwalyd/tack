namespace Tack.Windows.Clipboard;

/// <summary>
/// What putting a snapshot back writes, in order. The restored copy is kept out
/// of clipboard history and the cloud clipboard, which already hold the
/// original, unless it carries its own markers for that. A copy that was
/// marked private goes back with every marker, whether or not their data could
/// be read, so it stays private.
/// </summary>
static class RestorePlan
{
    static readonly byte[] s_zero = [0, 0, 0, 0];

    /// <param name="formats">The snapshot's formats and their bytes.</param>
    /// <param name="marks">Which privacy markers the snapshot had, in the order of <see cref="PrivacyMarkers.Names"/>.</param>
    /// <param name="markers">The markers' format numbers, in the same order.</param>
    public static List<RestoreWrite> Of(
        IReadOnlyList<(uint Format, byte[] Bytes)> formats,
        IReadOnlyList<bool> marks,
        IReadOnlyList<uint> markers)
    {
        var isPrivate = marks.Contains(true);
        var writes = new List<RestoreWrite>();
        foreach (var (format, bytes) in formats)
        {
            if (!(isPrivate && markers.Contains(format)))
            {
                writes.Add(new RestoreWrite(format, bytes, true));
            }
        }
        if (isPrivate)
        {
            writes.AddRange(markers.Select(marker => new RestoreWrite(marker, s_zero, true)));
        }
        else if (formats.Count > 0)
        {
            foreach (var marker in markers.Skip(PrivacyMarkers.FirstAllowance))
            {
                if (marker != 0 && !formats.Any(f => f.Format == marker))
                {
                    writes.Add(new RestoreWrite(marker, s_zero, false));
                }
            }
        }
        return writes;
    }
}
