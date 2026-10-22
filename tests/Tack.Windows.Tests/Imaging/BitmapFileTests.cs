using System.Buffers.Binary;
using Tack.Windows.Imaging;

namespace Tack.Windows.Tests.Imaging;

public sealed class BitmapFileTests
{
    [Theory]
    // Plain 32-bit: the pixels follow the header.
    [InlineData(32, 0, 0, 14 + 40)]
    // Colour masks, three or four of them, sit between header and pixels.
    [InlineData(32, 3, 0, 14 + 40 + 12)]
    [InlineData(32, 6, 0, 14 + 40 + 16)]
    // A palette: every colour of the depth, unless the header says how many.
    [InlineData(8, 0, 0, 14 + 40 + 256 * 4)]
    [InlineData(8, 0, 16, 14 + 40 + 16 * 4)]
    [InlineData(24, 0, 0, 14 + 40)]
    public void The_file_header_points_past_header_masks_and_palette(int bits, int compression, int coloursUsed, int offset)
    {
        var dib = Dibs.Of(4, 4, bits, compression, new byte[2048], coloursUsed);

        var file = BitmapFile.For(dib);

        Assert.Equal("BM"u8.ToArray(), file[..2]);
        Assert.Equal(14 + dib.Length, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(2)));
        Assert.Equal(offset, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(10)));
        Assert.Equal(dib, file[14..]);
    }

    [Fact]
    public void A_version_5_header_holds_its_own_masks()
    {
        var dib = Dibs.Of(4, 4, bits: 32, compression: 3, new byte[64], headerSize: 124);

        Assert.Equal(14 + 124, BinaryPrimitives.ReadInt32LittleEndian(BitmapFile.For(dib).AsSpan(10)));
    }

    [Fact]
    public void An_old_core_header_has_three_byte_palette_entries()
    {
        var dib = Dibs.Core(4, 4, bits: 8, new byte[1024]);

        Assert.Equal(14 + 12 + 256 * 3, BinaryPrimitives.ReadInt32LittleEndian(BitmapFile.For(dib).AsSpan(10)));
    }

    [Fact]
    public void A_truncated_bitmap_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => BitmapFile.For([40, 0]));
        Assert.Throws<InvalidDataException>(() => BitmapFile.For(Dibs.Of(4, 4, 32, 0, [])[..30]));
        // A palette the bytes do not hold.
        Assert.Throws<InvalidDataException>(() => BitmapFile.For(Dibs.Of(4, 4, 8, 0, new byte[100])));
        // A header size no bitmap has.
        Assert.Throws<InvalidDataException>(() => BitmapFile.For(Dibs.Of(4, 4, 32, 0, new byte[64], headerSize: 20)));
    }
}
