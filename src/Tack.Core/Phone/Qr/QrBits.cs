namespace Tack.Core.Phone.Qr;

/// <summary>A growing string of bits, most significant first, that becomes codewords.</summary>
internal sealed class QrBits
{
    readonly List<bool> _bits = [];

    /// <summary>Bits written so far.</summary>
    public int Count => _bits.Count;

    /// <summary>Writes the low <paramref name="length"/> bits of <paramref name="value"/>.</summary>
    public void Append(int value, int length)
    {
        for (var i = length - 1; i >= 0; i--)
        {
            _bits.Add(((value >> i) & 1) != 0);
        }
    }

    /// <summary>The bits as bytes; a last partial byte is padded with zeros.</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[(Count + 7) / 8];
        for (var i = 0; i < Count; i++)
        {
            if (_bits[i])
            {
                bytes[i / 8] |= (byte)(0x80 >> (i % 8));
            }
        }
        return bytes;
    }
}
