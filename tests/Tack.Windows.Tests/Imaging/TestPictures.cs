using Tack.Windows.Imaging;

namespace Tack.Windows.Tests.Imaging;

/// <summary>Pictures made up in code for the imaging tests.</summary>
static class TestPictures
{
    /// <summary>A <paramref name="w"/> x <paramref name="h"/> picture of one colour.</summary>
    public static Picture Solid(int w, int h, byte r, byte g, byte b, byte a = 255)
    {
        var bgra = new byte[w * h * 4];
        for (var at = 0; at < bgra.Length; at += 4)
        {
            (bgra[at], bgra[at + 1], bgra[at + 2], bgra[at + 3]) = (b, g, r, a);
        }
        return new Picture(w, h, bgra);
    }

    /// <summary>A picture whose every pixel differs from its neighbours, opaque unless <paramref name="alpha"/> says otherwise.</summary>
    public static Picture Busy(int w, int h, Func<int, int, byte>? alpha = null)
    {
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var at = (y * w + x) * 4;
                (bgra[at], bgra[at + 1], bgra[at + 2], bgra[at + 3]) =
                    ((byte)(x * 7 + y), (byte)(y * 13), (byte)(x ^ y), alpha?.Invoke(x, y) ?? 255);
            }
        }
        return new Picture(w, h, bgra);
    }

    /// <summary>The size of a PNG or JPEG, decoded.</summary>
    public static async Task<(int Width, int Height)> SizeOf(byte[] encoded)
    {
        var picture = await PictureDecoder.DecodeAsync(encoded);
        return (picture.Width, picture.Height);
    }

    public static bool IsJpeg(byte[] bytes) => bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]);

    public static bool IsPng(byte[] bytes) => bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G']);
}
