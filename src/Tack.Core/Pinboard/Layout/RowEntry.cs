namespace Tack.Core.Pinboard.Layout;

/// <summary>One print as the row lays it out: its size (paper border included, logical px) and whether it is kept.</summary>
public readonly record struct RowEntry(double Width, double Height, bool Kept);
