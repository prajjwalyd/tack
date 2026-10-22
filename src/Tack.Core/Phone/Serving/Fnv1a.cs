using System.Globalization;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// The 64-bit FNV-1a digest the phone board uses to name content: a picture's
/// <c>?v=</c> and the board's ETag. Not a security measure, only a cheap,
/// stable fingerprint that changes when the bytes do.
/// </summary>
internal static class Fnv1a
{
    const ulong Offset = 0xcbf29ce484222325;
    const ulong Prime = 0x100000001b3;

    public static ulong Hash(ReadOnlySpan<byte> bytes)
    {
        var hash = Offset;
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= Prime;
        }
        return hash;
    }

    /// <summary>The digest as 16 lower-case hex digits.</summary>
    public static string Hex(ReadOnlySpan<byte> bytes) => Hash(bytes).ToString("x16", CultureInfo.InvariantCulture);
}
