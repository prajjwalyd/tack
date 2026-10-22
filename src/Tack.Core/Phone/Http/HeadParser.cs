using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace Tack.Core.Phone.Http;

/// <summary>
/// Reads a request head from the bytes received so far, as strictly as Tack
/// 0.2 did: a request line of a method, a target and HTTP/1.0 or 1.1 split by
/// single spaces; then headers, each a name, a colon and a value; each line
/// ending in CRLF or a bare LF; the head ending at a blank line. Lines are
/// checked as they arrive, so a refused head is known before it ends. Pure, so
/// the connection only feeds it bytes.
/// </summary>
public static class HeadParser
{
    /// <summary>
    /// The head in <paramref name="bytes"/>, if they hold a whole one. Over
    /// <see cref="HttpLimits.MaxHead"/> bytes without the blank line, or over
    /// <see cref="HttpLimits.MaxHeaders"/> headers: HeadTooLarge. A line that is
    /// not HTTP, a value that is not UTF-8 or a Content-Length that is not plain
    /// digits: Malformed. Any Transfer-Encoding: LengthRequired.
    /// </summary>
    public static HeadParse Parse(ReadOnlySpan<byte> bytes)
    {
        var at = SkipEmptyLines(bytes);
        Range? request = null;
        var headers = new List<Range>();
        while (true)
        {
            var newline = bytes[at..].IndexOf((byte)'\n');
            if (newline < 0)
            {
                return bytes.Length >= HttpLimits.MaxHead ? HeadParse.Failed(HeadError.HeadTooLarge) : HeadParse.Partial;
            }
            var next = at + newline + 1;
            if (next > HttpLimits.MaxHead)
            {
                return HeadParse.Failed(HeadError.HeadTooLarge);
            }
            var end = newline > 0 && bytes[at + newline - 1] == '\r' ? at + newline - 1 : at + newline;
            var line = new Range(at, end);
            at = next;
            if (request == null)
            {
                if (!IsRequestLine(bytes[line]))
                {
                    return HeadParse.Failed(HeadError.Malformed);
                }
                request = line;
            }
            else if (end == line.Start.Value)
            {
                return Build(bytes, request.Value, headers, at);
            }
            else if (headers.Count == HttpLimits.MaxHeaders)
            {
                return HeadParse.Failed(HeadError.HeadTooLarge);
            }
            else if (!IsHeaderLine(bytes[line]))
            {
                return HeadParse.Failed(HeadError.Malformed);
            }
            else
            {
                headers.Add(line);
            }
        }
    }

    /// <summary>
    /// Plain decimal digits only, as a non-negative length; null for anything
    /// else ("-1", "+5", "1e3", "", too large).
    /// </summary>
    public static long? ParseLength(string text) =>
        text.Length > 0 && text.All(char.IsAsciiDigit)
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var length)
            ? length
            : null;

    /// <summary>The head from lines already checked: its strings, then the headers that decide the body.</summary>
    static HeadParse Build(ReadOnlySpan<byte> bytes, Range request, List<Range> lines, int length)
    {
        var line = bytes[request];
        var space = line.IndexOf((byte)' ');
        var method = Encoding.ASCII.GetString(line[..space]);
        var rest = line[(space + 1)..];
        var target = rest[..rest.IndexOf((byte)' ')];
        var query = target.IndexOf((byte)'?');
        var path = query < 0 ? target : target[..query];
        if (!Utf8.IsValid(path))
        {
            return HeadParse.Failed(HeadError.Malformed);
        }
        var headers = new List<KeyValuePair<string, string>>(lines.Count);
        foreach (var range in lines)
        {
            var header = bytes[range];
            var colon = header.IndexOf((byte)':');
            var value = header[(colon + 1)..];
            if (!Utf8.IsValid(value))
            {
                return HeadParse.Failed(HeadError.Malformed);
            }
            headers.Add(new(Encoding.ASCII.GetString(header[..colon]), Encoding.UTF8.GetString(value).Trim()));
        }
        var head = new RequestHead(method, Encoding.UTF8.GetString(path), headers, 0, bytes[length..].ToArray());
        if (head.Header("Transfer-Encoding") != null)
        {
            return HeadParse.Failed(HeadError.LengthRequired);
        }
        if (head.Header("Content-Length") is { } text)
        {
            if (ParseLength(text) is not { } contentLength)
            {
                return HeadParse.Failed(HeadError.Malformed);
            }
            head = head with { ContentLength = contentLength };
        }
        return HeadParse.Complete(head, length);
    }

    /// <summary>Blank lines before the request line are skipped, as a lenient server should.</summary>
    static int SkipEmptyLines(ReadOnlySpan<byte> bytes)
    {
        var at = 0;
        while (at < bytes.Length)
        {
            if (bytes[at] == '\n')
            {
                at++;
            }
            else if (bytes[at] == '\r' && at + 1 < bytes.Length && bytes[at + 1] == '\n')
            {
                at += 2;
            }
            else
            {
                break;
            }
        }
        return at;
    }

    /// <summary>"GET /path HTTP/1.1": a token, a target of visible bytes, a version, single spaces between.</summary>
    static bool IsRequestLine(ReadOnlySpan<byte> line)
    {
        var first = line.IndexOf((byte)' ');
        if (first <= 0)
        {
            return false;
        }
        var rest = line[(first + 1)..];
        var second = rest.IndexOf((byte)' ');
        if (second <= 0)
        {
            return false;
        }
        var version = rest[(second + 1)..];
        return IsToken(line[..first])
            && IsTarget(rest[..second])
            && (version.SequenceEqual("HTTP/1.1"u8) || version.SequenceEqual("HTTP/1.0"u8));
    }

    /// <summary>"Name: value": a token straight before the colon, and a value with no control bytes but tab.</summary>
    static bool IsHeaderLine(ReadOnlySpan<byte> line)
    {
        var colon = line.IndexOf((byte)':');
        if (colon <= 0 || !IsToken(line[..colon]))
        {
            return false;
        }
        foreach (var b in line[(colon + 1)..])
        {
            if (b != '\t' && (b < 0x20 || b == 0x7F))
            {
                return false;
            }
        }
        return true;
    }

    static bool IsTarget(ReadOnlySpan<byte> target)
    {
        foreach (var b in target)
        {
            if (b <= 0x20 || b == 0x7F)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>HTTP's token: letters, digits and !#$%&amp;'*+-.^_`|~.</summary>
    static bool IsToken(ReadOnlySpan<byte> token)
    {
        foreach (var b in token)
        {
            if (!char.IsAsciiLetterOrDigit((char)b) && "!#$%&'*+-.^_`|~"u8.IndexOf(b) < 0)
            {
                return false;
            }
        }
        return true;
    }
}
