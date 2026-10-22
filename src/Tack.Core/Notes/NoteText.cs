using System.Text;

namespace Tack.Core.Notes;

/// <summary>
/// Turning text into a note: normalised, clamped, and a link when the whole note
/// is one web address. Only http and https links are ever made, because only
/// links are handed to the browser.
/// </summary>
public static class NoteText
{
    /// <summary>The most text a note holds, in bytes of UTF-8.</summary>
    public const int MaxNoteBytes = 20 * 1024;

    /// <summary>A link longer than this, in bytes of UTF-8, is kept as plain text.</summary>
    public const int MaxLinkBytes = 4096;

    /// <summary>
    /// What a note made of <paramref name="text"/> holds, or null if there is
    /// nothing to pin (only whitespace). Line breaks become \n, blank lines around
    /// the text go, and text past <see cref="MaxNoteBytes"/> is cut.
    /// </summary>
    public static NoteBody? Body(string text)
    {
        var normal = Normalise(text);
        if (normal.Length == 0)
        {
            return null;
        }
        var (clamped, truncated) = Clamp(normal);
        var link = truncated ? null : LinkOf(clamped);
        return new NoteBody(clamped, link, link is null ? null : DomainOf(link), truncated);
    }

    /// <summary>
    /// The URL, when the whole of <paramref name="text"/> is one web link:
    /// http:// or https:// (any case) and a host, with no whitespace, quotes or
    /// control characters anywhere. A bare www. address counts too, as https.
    /// </summary>
    public static string? LinkOf(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || Encoding.UTF8.GetByteCount(text) > MaxLinkBytes)
        {
            return null;
        }
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune) || rune.Value is '"' or '<' or '>' or '`' or '\\')
            {
                return null;
            }
        }
        var url = StartsWithAscii(text, "https://") || StartsWithAscii(text, "http://") ? text
            : StartsWithAscii(text, "www.") ? "https://" + text
            : null;
        return url != null && DomainOf(url) != null ? url : null;
    }

    /// <summary>
    /// The host of an http(s) URL, lowercase, without "www.", a port or a user
    /// name: "https://www.GitHub.com:443/a?b" gives "github.com". Null if there
    /// is no plausible host.
    /// </summary>
    public static string? DomainOf(string url)
    {
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
        {
            return null;
        }
        var rest = url[(scheme + 3)..];
        var authority = rest.IndexOfAny(['/', '?', '#']) is >= 0 and var end ? rest[..end] : rest;
        var host = authority.LastIndexOf('@') is >= 0 and var user ? authority[(user + 1)..] : authority;
        // An IPv6 literal keeps its brackets and loses only the port.
        host = host.StartsWith('[')
            ? host.IndexOf(']') is >= 0 and var close ? host[..(close + 1)] : host
            : host.IndexOf(':') is >= 0 and var colon ? host[..colon] : host;
        host = AsciiLower(host);
        if (host.Length == 0 || host[0] == '.' || host[^1] == '.')
        {
            return null;
        }
        foreach (var rune in host.EnumerateRunes())
        {
            if (!(Rune.IsLetterOrDigit(rune) || Rune.IsNumber(rune) || rune.Value is '.' or '-' or '_' or '[' or ']' or ':'))
            {
                return null;
            }
        }
        return host.StartsWith("www.", StringComparison.Ordinal) && host.Length > 4 ? host[4..] : host;
    }

    /// <summary>
    /// \r\n and \r become \n, other control characters go, and so do blank lines
    /// before the text and all whitespace after it. The first line's indentation
    /// stays: it may be code.
    /// </summary>
    static string Normalise(string text)
    {
        var kept = new StringBuilder(text.Length);
        foreach (var c in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            // A copied picture's placeholder character and NULs are not text.
            if (!char.IsControl(c) || c is '\n' or '\t')
            {
                kept.Append(c);
            }
        }
        var trimmed = kept.ToString().TrimEnd();
        var start = 0;
        while (start < trimmed.Length)
        {
            var lineEnd = trimmed.IndexOf('\n', start) is >= 0 and var newline ? newline + 1 : trimmed.Length;
            if (!string.IsNullOrWhiteSpace(trimmed[start..lineEnd]))
            {
                break;
            }
            start = lineEnd;
        }
        return trimmed[start..];
    }

    /// <summary>Cuts <paramref name="text"/> to at most <see cref="MaxNoteBytes"/> of UTF-8, at a character boundary.</summary>
    static (string Text, bool Truncated) Clamp(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) <= MaxNoteBytes)
        {
            return (text, false);
        }
        var (bytes, end) = (0, 0);
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > MaxNoteBytes)
            {
                break;
            }
            bytes += rune.Utf8SequenceLength;
            end += rune.Utf16SequenceLength;
        }
        return (text[..end].TrimEnd(), true);
    }

    /// <summary>A prefix match that folds ASCII letters only, so no other character can pass for one.</summary>
    static bool StartsWithAscii(string text, string lowerPrefix)
    {
        if (text.Length < lowerPrefix.Length)
        {
            return false;
        }
        for (var i = 0; i < lowerPrefix.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) != lowerPrefix[i] || !char.IsAscii(text[i]))
            {
                return false;
            }
        }
        return true;
    }

    static string AsciiLower(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsAsciiLetterUpper(source[i]) ? (char)(source[i] + 32) : source[i];
            }
        });
}
