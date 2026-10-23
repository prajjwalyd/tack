using System.Buffers.Binary;

namespace Tack.Windows.Tests.Imaging;

/// <summary>.bmp files made up in code, as Windows hands over a bitmap copied to the clipboard.</summary>
static class BmpFiles
{
    /// <summary>A 32-bit, uncompressed, bottom-up bitmap.</summary>
    public static byte[] Bgrx(int w, int h, byte[] pixels) => Of(w, h, bits: 32, compression: 0, pixels);

    /// <summary>
    /// A bitmap with a BITMAPINFOHEADER; a negative <paramref name="h"/> means
    /// top-down rows. <paramref name="masks"/> go between the header and the pixels.
    /// </summary>
    public static byte[] Of(int w, int h, int bits, int compression, byte[] pixels, byte[]? masks = null)
    {
        const int fileHeader = 14, infoHeader = 40;
        masks ??= [];
        var file = new byte[fileHeader + infoHeader + masks.Length + pixels.Length];
        var span = file.AsSpan();
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], fileHeader + infoHeader + masks.Length);
        var header = span[fileHeader..];
        BinaryPrimitives.WriteInt32LittleEndian(header, infoHeader);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], w);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], h);
        BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[14..], (short)bits);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], compression);
        masks.CopyTo(header[infoHeader..]);
        pixels.CopyTo(header[(infoHeader + masks.Length)..]);
        return file;
    }
}
