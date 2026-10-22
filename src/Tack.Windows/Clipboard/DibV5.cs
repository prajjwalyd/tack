using System.Buffers.Binary;
using Tack.Windows.Imaging;

namespace Tack.Windows.Clipboard;

/// <summary>
/// CF_DIBV5 for a picture, built by hand: a BITMAPV5HEADER for 32-bit sRGB
/// with alpha, its colour masks inside the header only (as other apps expect),
/// then the pixels as BGRA, bottom row first.
/// </summary>
static class DibV5
{
    public const int HeaderSize = 124;

    const uint BitFields = 3;
    const uint SRgb = 0x7352_4742;
    const uint PictureIntent = 4;

    public static byte[] Of(Picture picture)
    {
        var row = picture.Width * 4;
        var size = row * picture.Height;
        var dib = new byte[HeaderSize + size];
        // Size, width, height, then 1 plane and 32 bits per pixel.
        Put(dib, 0, HeaderSize);
        Put(dib, 4, (uint)picture.Width);
        Put(dib, 8, (uint)picture.Height);
        Put(dib, 12, 1 | (32 << 16));
        Put(dib, 16, BitFields);
        Put(dib, 20, (uint)size);
        // Red, green, blue and alpha masks, then the colour space.
        Put(dib, 40, 0x00FF_0000);
        Put(dib, 44, 0x0000_FF00);
        Put(dib, 48, 0x0000_00FF);
        Put(dib, 52, 0xFF00_0000);
        Put(dib, 56, SRgb);
        // The endpoints and gamma between are unused with sRGB.
        Put(dib, 108, PictureIntent);
        var pixels = picture.Bgra.AsSpan();
        for (var y = 0; y < picture.Height; y++)
        {
            pixels.Slice(y * row, row).CopyTo(dib.AsSpan(HeaderSize + (picture.Height - 1 - y) * row));
        }
        return dib;
    }

    static void Put(byte[] dib, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(at), value);
}
