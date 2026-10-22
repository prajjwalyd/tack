using System.Globalization;
using System.Text;

namespace Tack.Core.Phone.Http;

/// <summary>
/// The text of a response's head, exactly as Tack 0.2 wrote it: the status
/// line, the type and length, Connection: close, the headers every response
/// carries (the page's CSP, nosniff, no framing, no referrer, same-origin
/// opener and resources), Cache-Control: no-store unless the response sets
/// its own, then the response's own headers in order, then the blank line.
/// </summary>
public static class ResponseHead
{
    /// <summary>The interim answer to Expect: 100-continue, sent before the body is read.</summary>
    public const string Continue = "HTTP/1.1 100 Continue\r\n\r\n";

    /// <summary>The head of <paramref name="response"/>, with <paramref name="csp"/> as its CSP.</summary>
    public static string Text(PhoneResponse response, string csp)
    {
        var head = new StringBuilder(512);
        var invariant = CultureInfo.InvariantCulture;
        head.Append(invariant, $"HTTP/1.1 {response.Status} {Reason(response.Status)}\r\n")
            .Append(invariant, $"Content-Type: {response.ContentType}\r\n")
            .Append(invariant, $"Content-Length: {response.Length}\r\n")
            .Append("Connection: close\r\n")
            .Append(invariant, $"Content-Security-Policy: {csp}\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("X-Frame-Options: DENY\r\n")
            .Append("Referrer-Policy: no-referrer\r\n")
            .Append("Cross-Origin-Opener-Policy: same-origin\r\n")
            .Append("Cross-Origin-Resource-Policy: same-origin\r\n");
        if (response.Header("Cache-Control") == null)
        {
            head.Append("Cache-Control: no-store\r\n");
        }
        foreach (var (name, value) in response.Headers)
        {
            head.Append(invariant, $"{name}: {value}\r\n");
        }
        return head.Append("\r\n").ToString();
    }

    /// <summary>The reason phrase for each status the board sends; empty for any other.</summary>
    public static string Reason(int status) => status switch
    {
        200 => "OK",
        202 => "Accepted",
        304 => "Not Modified",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        411 => "Length Required",
        413 => "Payload Too Large",
        415 => "Unsupported Media Type",
        421 => "Misdirected Request",
        422 => "Unprocessable Content",
        429 => "Too Many Requests",
        431 => "Request Header Fields Too Large",
        503 => "Service Unavailable",
        _ => string.Empty,
    };
}
