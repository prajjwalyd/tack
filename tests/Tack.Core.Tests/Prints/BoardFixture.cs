using Tack.Core.Prints;
using Tack.Core.Notes;
using Tack.Core.Thumbnails;

namespace Tack.Core.Tests.Prints;

/// <summary>Helpers shared by the board's tests: a fixed "now" and prints made to order.</summary>
static class BoardFixture
{
    /// <summary>A fixed "now" on the wall clock, and one minute, in ms.</summary>
    public const long Now = 1_800_000_000_000;
    public const long Min = 60_000;

    /// <summary>An arbitrary point on the monotonic clock.</summary>
    public static readonly TimeSpan Start = TimeSpan.FromHours(1);

    /// <summary>A picture of this size; all pictures of one size here have the same pixels.</summary>
    public static Thumb Thumb(int width, int height) => WithHash(width, height, (ulong)width << 32 | (uint)height);

    public static Thumb WithHash(int width, int height, ulong? hash) => new([], width, height, hash);

    public static string PathOf(string name) => $@"C:\nowhere\{name}.png";

    public static string NotePath(string name) => $@"C:\nowhere\Notes\{name}.txt";

    public static NoteBody Note(string text) => new(text, Link: null, Domain: null, Truncated: false);

    /// <summary>A live screenshot of the usual size, <paramref name="n"/> minutes after Now.</summary>
    public static PinOutcome Live(Board board, string name, Origin origin, long n) =>
        board.Pin(PathOf(name), Thumb(1920, 1080), origin, new Arrival.Live(Start + TimeSpan.FromMinutes(n)), Now + n * Min);

    /// <summary>Pins <paramref name="count"/> live screenshot files of different sizes, a minute apart from minute <paramref name="from"/>.</summary>
    public static void PinMany(Board board, long from, long count)
    {
        for (var n = from; n < from + count; n++)
        {
            board.Pin(PathOf(n.ToString()), Thumb((int)n + 1, 1), Origin.Folder, new Arrival.Live(Start), Now + n * Min);
        }
    }

    public static PinOutcome Restored(Board board, string name, long pinnedAt, long? keptAt) =>
        board.Pin(PathOf(name), Thumb(800, 600), Origin.Folder, new Arrival.Restored(pinnedAt, keptAt), Now);

    public static PinOutcome LiveNote(Board board, string name, string text, long n) =>
        board.PinNote(NotePath(name), Note(text), new Arrival.Live(Start + TimeSpan.FromMinutes(n)), Now + n * Min);

    public static string[] Ids(Board board) => [.. board.Prints.Select(p => p.Id)];

    public static string[] Ids(IEnumerable<Print> prints) => [.. prints.Select(p => p.Id)];
}
