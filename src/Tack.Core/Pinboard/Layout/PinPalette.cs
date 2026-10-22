namespace Tack.Core.Pinboard.Layout;

/// <summary>
/// The pins' colours: a stable first colour from each print's id, and no two
/// neighbouring unkept pins of one colour. On a clash the newer print changes,
/// so pins already seen keep theirs.
/// </summary>
public static class PinPalette
{
    const int Count = 5;

    /// <summary>A stable first colour from <paramref name="id"/>: FNV-1a, 32 bit, over its UTF-16 code units.</summary>
    public static PinColor ColorFor(string id)
    {
        var hash = 2166136261u;
        foreach (var c in id)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }
        return (PinColor)(hash % Count);
    }

    /// <summary>
    /// The row's colours, in order, with each clash between unkept neighbours
    /// settled: the one not yet settled (the right one if neither or both are)
    /// takes the first palette colour that neither of the two before it, nor
    /// the one after, nor its neighbour has. Afterwards every pin counts as settled.
    /// </summary>
    public static PinColor[] Fix(IReadOnlyList<PinSlot> row)
    {
        var colors = row.Select(slot => slot.Color).ToArray();
        for (var i = 1; i < colors.Length; i++)
        {
            var (a, b) = (row[i - 1], row[i]);
            if (a.Kept || b.Kept || colors[i - 1] != colors[i])
            {
                continue;
            }
            var victim = b.Settled && !a.Settled ? i - 1 : i;
            PinColor?[] near = [i >= 2 ? colors[i - 2] : null, colors[i - 1], colors[i], i + 1 < colors.Length ? colors[i + 1] : null];
            colors[victim] = Enum.GetValues<PinColor>().First(c => !near.Contains(c));
        }
        return colors;
    }
}
