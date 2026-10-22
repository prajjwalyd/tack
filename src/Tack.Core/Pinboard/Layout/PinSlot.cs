namespace Tack.Core.Pinboard.Layout;

/// <summary>
/// One print's pin as the palette sees it: its colour, whether it is kept
/// (a brass pin, which never clashes) and whether its colour has been seen.
/// </summary>
public readonly record struct PinSlot(PinColor Color, bool Kept, bool Settled);
