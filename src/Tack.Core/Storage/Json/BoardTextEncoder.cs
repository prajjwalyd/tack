using System.Text;
using System.Text.Encodings.Web;

namespace Tack.Core.Storage.Json;

/// <summary>
/// Escapes text in board.json exactly as Tack 0.2 did, so a save of an
/// unchanged board gives the same bytes. Only a quote, a backslash and the
/// control characters below U+0020 are escaped: \b \f \n \r \t by name, the
/// rest as \u00xx in lower-case hex. Everything else, U+2028, U+2029 and
/// emoji included, is written as it is, in UTF-8. The built-in encoders escape
/// more than that (characters outside the Basic Multilingual Plane, at least),
/// which would change a path with an emoji in it.
/// </summary>
internal sealed class BoardTextEncoder : JavaScriptEncoder
{
    public static BoardTextEncoder Instance { get; } = new();

    BoardTextEncoder()
    {
    }

    /// <summary>The longest escape, \u00xx.</summary>
    public override int MaxOutputCharactersPerInputCharacter => 6;

    public override bool WillEncode(int unicodeScalar) => unicodeScalar is < 0x20 or '"' or '\\';

    /// <summary>
    /// The first character to escape, or -1. A lone surrogate counts too: it is
    /// not text, so the encoder writes U+FFFD in its place.
    /// </summary>
    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        var chars = new ReadOnlySpan<char>(text, textLength);
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsHighSurrogate(c) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1]))
            {
                i++;
                continue;
            }
            if (WillEncode(c) || char.IsSurrogate(c))
            {
                return i;
            }
        }
        return -1;
    }

    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
    {
        var destination = new Span<char>(buffer, bufferLength);
        if (!WillEncode(unicodeScalar))
        {
            return new Rune(unicodeScalar).TryEncodeToUtf16(destination, out numberOfCharactersWritten);
        }
        ReadOnlySpan<char> escape = unicodeScalar switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\b' => "\\b",
            '\f' => "\\f",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => $"\\u{unicodeScalar:x4}",
        };
        if (!escape.TryCopyTo(destination))
        {
            numberOfCharactersWritten = 0;
            return false;
        }
        numberOfCharactersWritten = escape.Length;
        return true;
    }
}
