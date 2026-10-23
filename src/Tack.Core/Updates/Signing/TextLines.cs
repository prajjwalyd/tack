namespace Tack.Core.Updates.Signing;

/// <summary>
/// Splits key and signature text into lines the way Tack 0.2 did: at "\n" or
/// "\r\n", with no empty line after a final line ending. A lone "\r" stays
/// part of its line, and so of what is signed or decoded.
/// </summary>
internal static class TextLines
{
    public static List<string> Split(string text)
    {
        var lines = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var newline = text.IndexOf('\n', start);
            if (newline < 0)
            {
                lines.Add(text[start..]);
                break;
            }

            var end = newline > start && text[newline - 1] == '\r' ? newline - 1 : newline;
            lines.Add(text[start..end]);
            start = newline + 1;
        }

        return lines;
    }
}
