namespace Tack.Core.Pinboard.Layout;

/// <summary>
/// Rounding as the board always rounded: to the nearest whole number, halves
/// up toward positive infinity (-2.5 is -2), so prints left of the board's
/// centre land on the same pixels as those right of it.
/// </summary>
static class HalfUp
{
    public static double Round(double value) => Math.Floor(value + 0.5);

    public static int Px(double value) => (int)Round(value);
}
