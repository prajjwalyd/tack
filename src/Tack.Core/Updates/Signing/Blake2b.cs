using System.Buffers.Binary;
using System.Numerics;

namespace Tack.Core.Updates.Signing;

/// <summary>
/// BLAKE2b-512 (RFC 7693), unkeyed, fed in pieces: minisign signs this hash of
/// a file rather than the file itself, so an installer is hashed as it is read
/// and never held in memory whole.
/// </summary>
internal sealed class Blake2b
{
    /// <summary>The length of the hash, in bytes.</summary>
    public const int HashSize = 64;

    const int BlockSize = 128;

    /// <summary>The initial state: the same constants as SHA-512's.</summary>
    static readonly ulong[] Iv =
    [
        0x6A09E667F3BCC908, 0xBB67AE8584CAA73B, 0x3C6EF372FE94F82B, 0xA54FF53A5F1D36F1,
        0x510E527FADE682D1, 0x9B05688C2B3E6C1F, 0x1F83D9ABFB41BD6B, 0x5BE0CD19137E2179,
    ];

    /// <summary>
    /// The order each round reads the block's sixteen words in. BLAKE2b has
    /// twelve rounds and ten orders; rounds 10 and 11 reuse orders 0 and 1.
    /// </summary>
    static readonly byte[][] Sigma =
    [
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
        [14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3],
        [11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4],
        [7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8],
        [9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13],
        [2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9],
        [12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11],
        [13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10],
        [6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5],
        [10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0],
    ];

    readonly ulong[] _h = new ulong[8];
    readonly byte[] _block = new byte[BlockSize];
    int _filled;
    // The 128-bit count of bytes compressed so far, low and high halves.
    ulong _countLow;
    ulong _countHigh;

    public Blake2b()
    {
        Iv.CopyTo(_h, 0);
        // The parameter block's first word: fanout 1, depth 1, no key, a 64-byte hash.
        _h[0] ^= 0x01010000UL | HashSize;
    }

    /// <summary>The hash of <paramref name="data"/> in one go.</summary>
    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        var hasher = new Blake2b();
        hasher.Update(data);
        var hash = new byte[HashSize];
        hasher.Finish(hash);
        return hash;
    }

    /// <summary>Adds <paramref name="data"/> to what is hashed.</summary>
    public void Update(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            // A full block is compressed only once more data follows it: the
            // last block, full or not, is compressed in Finish, marked as last.
            if (_filled == BlockSize)
            {
                Count(BlockSize);
                Compress(last: false);
                _filled = 0;
            }

            var take = Math.Min(BlockSize - _filled, data.Length);
            data[..take].CopyTo(_block.AsSpan(_filled));
            _filled += take;
            data = data[take..];
        }
    }

    /// <summary>Writes the hash of everything added into <paramref name="hash"/>, 64 bytes. Use once.</summary>
    public void Finish(Span<byte> hash)
    {
        // The last block is padded with zeros; the count says how much of it is data.
        Count(_filled);
        _block.AsSpan(_filled).Clear();
        Compress(last: true);
        for (var i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(hash[(i * 8)..], _h[i]);
        }
    }

    void Count(int bytes)
    {
        _countLow += (ulong)bytes;
        if (_countLow < (ulong)bytes)
        {
            _countHigh++;
        }
    }

    /// <summary>The compression function F: mixes the buffered block into the state.</summary>
    void Compress(bool last)
    {
        Span<ulong> m = stackalloc ulong[16];
        Span<ulong> v = stackalloc ulong[16];
        for (var i = 0; i < 16; i++)
        {
            m[i] = BinaryPrimitives.ReadUInt64LittleEndian(_block.AsSpan(i * 8));
        }

        // The working vector: the state, then the initial state with the count
        // folded into words 12 and 13 and word 14 inverted on the last block.
        _h.CopyTo(v);
        Iv.CopyTo(v[8..]);
        v[12] ^= _countLow;
        v[13] ^= _countHigh;
        if (last)
        {
            v[14] = ~v[14];
        }

        for (var round = 0; round < 12; round++)
        {
            var s = Sigma[round % 10];
            // Four column steps, then four diagonal steps.
            Mix(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
            Mix(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
            Mix(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
            Mix(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
            Mix(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
            Mix(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
            Mix(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
            Mix(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
        }

        for (var i = 0; i < 8; i++)
        {
            _h[i] ^= v[i] ^ v[i + 8];
        }
    }

    /// <summary>The mixing function G: adds two message words into four state words, rotating by 32, 24, 16 and 63.</summary>
    static void Mix(Span<ulong> v, int a, int b, int c, int d, ulong x, ulong y)
    {
        v[a] += v[b] + x;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 32);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 24);
        v[a] += v[b] + y;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 63);
    }
}
