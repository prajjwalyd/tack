using System.Buffers.Binary;

namespace Tack.Windows.Tests.Imaging;

/// <summary>CF_DIB bitmaps made up in code: a BITMAPINFOHEADER, then whatever follows it.</summary>
static class Dibs
{
    /// <summary>A 32-bit, uncompressed, bottom-up bitmap.</summary>
    public static byte[] Bgrx(int w, int h, byte[] pixels) => Of(w, h, bits: 32, compression: 0, pixels);

    /// <summary>A bitmap with this header; a negative <paramref name="h"/> means top-down rows.</summary>
    public static byte[] Of(int w, int h, int bits, int compression, byte[] pixels, int coloursUsed = 0, int headerSize = 40)
    {
        var dib = new byte[headerSize + pixels.Length];
        var header = dib.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(header, headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], w);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], h);
        BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[14..], (short)bits);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], compression);
        BinaryPrimitives.WriteInt32LittleEndian(header[32..], coloursUsed);
        pixels.CopyTo(dib, headerSize);
        return dib;
    }

    /// <summary>A bitmap with the old 12-byte BITMAPCOREHEADER.</summary>
    public static byte[] Core(int w, int h, int bits, byte[] rest)
    {
        var dib = new byte[12 + rest.Length];
        var header = dib.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(header, 12);
        BinaryPrimitives.WriteInt16LittleEndian(header[4..], (short)w);
        BinaryPrimitives.WriteInt16LittleEndian(header[6..], (short)h);
        BinaryPrimitives.WriteInt16LittleEndian(header[8..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[10..], (short)bits);
        rest.CopyTo(dib, 12);
        return dib;
    }
}
