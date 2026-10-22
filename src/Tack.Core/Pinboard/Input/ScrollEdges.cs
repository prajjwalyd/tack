namespace Tack.Core.Pinboard.Input;

/// <summary>Which sides of the row have more prints out of sight, so fade.</summary>
public readonly record struct ScrollEdges(bool Left, bool Right);
