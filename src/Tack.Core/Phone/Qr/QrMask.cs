namespace Tack.Core.Phone.Qr;

/// <summary>The eight mask patterns, which break up the data so a camera reads it well.</summary>
internal static class QrMask
{
    public const int Count = 8;

    /// <summary>Whether <paramref name="mask"/> flips the module in column <paramref name="x"/>, row <paramref name="y"/>.</summary>
    public static bool Flips(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => ((x / 3) + (y / 2)) % 2 == 0,
        5 => ((x * y % 2) + (x * y % 3)) == 0,
        6 => ((x * y % 2) + (x * y % 3)) % 2 == 0,
        _ => (((x + y) % 2) + (x * y % 3)) % 2 == 0,
    };
}
