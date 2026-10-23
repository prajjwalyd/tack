namespace Tack.Core.Updates.Signing;

/// <summary>
/// Standard base64 (RFC 4648, with + and /), read strictly as Tack 0.2 read
/// keys and signatures: no whitespace, exactly the padding the length calls
/// for, and no stray bits in the last character. Anything else is refused,
/// so each value has one spelling.
/// </summary>
internal static class StrictBase64
{
    /// <summary>The bytes <paramref name="text"/> encodes, or null when it is not strict base64.</summary>
    public static byte[]? Decode(ReadOnlySpan<char> text)
    {
        var bytes = new byte[text.Length * 3 / 4];
        var count = 0;
        var bits = 0;
        var pending = 0;
        var end = text.Length;
        for (var i = 0; i < text.Length; i++)
        {
            var digit = Digit(text[i]);
            if (digit < 0)
            {
                // Data ends here; what follows must be padding, checked below.
                end = i;
                break;
            }

            // Six more bits in; a byte out whenever eight are waiting.
            bits = ((bits << 6) | digit) & 0xFFFF;
            pending += 6;
            if (pending >= 8)
            {
                pending -= 8;
                bytes[count++] = (byte)(bits >> pending);
            }
        }

        // Left over: none, or 2 or 4 bits (one or two '=' due), all zero.
        if (pending > 4 || (bits & ((1 << pending) - 1)) != 0)
        {
            return null;
        }

        var padding = text[end..];
        if (padding.Length != pending / 2 || padding.ContainsAnyExcept('='))
        {
            return null;
        }

        return bytes[..count];
    }

    static int Digit(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a' + 26,
        >= '0' and <= '9' => c - '0' + 52,
        '+' => 62,
        '/' => 63,
        _ => -1,
    };
}
