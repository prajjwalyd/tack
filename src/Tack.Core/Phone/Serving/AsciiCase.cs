namespace Tack.Core.Phone.Serving;

/// <summary>
/// Case rules for Host and Origin: only ASCII letters fold, as Tack 0.2 had
/// it, so no other character can pass for one (a dotless i is not an i).
/// </summary>
internal static class AsciiCase
{
    /// <summary><paramref name="text"/> with A to Z lowered and everything else as it is.</summary>
    public static string Lower(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsAsciiLetterUpper(source[i]) ? (char)(source[i] + 32) : source[i];
            }
        });

    /// <summary>Whether the two are the same text when ASCII letters are folded.</summary>
    public static bool Same(string a, string b) => a.Length == b.Length && Lower(a) == Lower(b);
}
