using System.Buffers.Binary;

namespace Tack.Windows.Imaging;

/// <summary>
/// Turns a CF_DIB bitmap (a header and its pixels, as the clipboard holds
/// them) into the bytes of a .bmp file, which Windows Imaging can decode: the
/// file header only adds where the pixels start, after the header, any colour
/// masks and any palette.
/// </summary>
static class BitmapFile
{
    const int FileHeaderSize = 14;
    const int CoreHeaderSize = 12;
    const int InfoHeaderSize = 40;
    const int LargestHeaderSize = 124;
    const uint BitFields = 3;
    const uint AlphaBitFields = 6;

    /// <summary>The .bmp file for <paramref name="dib"/>. Throws <see cref="InvalidDataException"/> if it is not a whole bitmap header.</summary>
    public static byte[] For(ReadOnlySpan<byte> dib)
    {
        var offset = PixelOffset(dib);
        var file = new byte[FileHeaderSize + dib.Length];
        var header = file.AsSpan();
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header[10..], FileHeaderSize + offset);
        dib.CopyTo(header[FileHeaderSize..]);
        return file;
    }

    /// <summary>Where the pixels start within <paramref name="dib"/>.</summary>
    static int PixelOffset(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 4)
        {
            throw new InvalidDataException("The bitmap has no header.");
        }
        var size = BinaryPrimitives.ReadInt32LittleEndian(dib);
        if (size != CoreHeaderSize && size is < InfoHeaderSize or > LargestHeaderSize)
        {
            throw new InvalidDataException($"A bitmap header is never {size} bytes.");
        }
        if (dib.Length < size)
        {
            throw new InvalidDataException("The bitmap's header is cut short.");
        }
        long offset;
        if (size == CoreHeaderSize)
        {
            int bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[10..]);
            offset = size + (bits <= 8 ? 3L << bits : 0);
        }
        else
        {
            int bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
            var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
            var used = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
            // Only the plain info header keeps its masks after itself; later versions hold them inside.
            var masks = size != InfoHeaderSize ? 0 : compression switch
            {
                BitFields => 12,
                AlphaBitFields => 16,
                _ => 0,
            };
            var colours = used != 0 ? used : bits <= 8 ? 1L << bits : 0;
            offset = size + masks + colours * 4;
        }
        if (offset > dib.Length)
        {
            throw new InvalidDataException("The bitmap's palette is cut short.");
        }
        return (int)offset;
    }
}
