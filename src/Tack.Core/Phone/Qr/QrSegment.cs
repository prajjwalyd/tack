namespace Tack.Core.Phone.Qr;

/// <summary>The bytes from <paramref name="Start"/> up to <paramref name="End"/>, written in one mode.</summary>
internal readonly record struct QrSegment(QrMode Mode, int Start, int End)
{
    /// <summary>The characters alphanumeric mode holds, each worth its index.</summary>
    public const string AlphanumericChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    int Length => End - Start;

    /// <summary>Bits this segment takes in <paramref name="version"/>: mode, count and data.</summary>
    public int Bits(int version) => 4 + CountBits(version) + Mode switch
    {
        QrMode.Numeric => ((Length * 10) + 2) / 3,
        QrMode.Alphanumeric => ((Length * 11) + 1) / 2,
        _ => Length * 8,
    };

    /// <summary>Writes the segment's mode, count and data.</summary>
    public void Write(QrBits bits, byte[] text, int version)
    {
        bits.Append(Mode switch { QrMode.Numeric => 0b0001, QrMode.Alphanumeric => 0b0010, _ => 0b0100 }, 4);
        bits.Append(Length, CountBits(version));
        switch (Mode)
        {
            case QrMode.Numeric:
                for (var i = Start; i < End; i += 3)
                {
                    var digits = Math.Min(3, End - i);
                    var value = 0;
                    for (var k = i; k < i + digits; k++)
                    {
                        value = (value * 10) + (text[k] - '0');
                    }
                    bits.Append(value, (digits * 3) + 1);
                }
                break;
            case QrMode.Alphanumeric:
                for (var i = Start; i < End; i += 2)
                {
                    var first = AlphanumericChars.IndexOf((char)text[i]);
                    if (i + 1 < End)
                    {
                        bits.Append((first * 45) + AlphanumericChars.IndexOf((char)text[i + 1]), 11);
                    }
                    else
                    {
                        bits.Append(first, 6);
                    }
                }
                break;
            default:
                for (var i = Start; i < End; i++)
                {
                    bits.Append(text[i], 8);
                }
                break;
        }
    }

    /// <summary>Bits of the character count, which grow at versions 10 and 27.</summary>
    int CountBits(int version)
    {
        var band = version <= 9 ? 0 : version <= 26 ? 1 : 2;
        return Mode switch
        {
            QrMode.Numeric => 10 + (band * 2),
            QrMode.Alphanumeric => 9 + (band * 2),
            _ => band == 0 ? 8 : 16,
        };
    }
}
