using System.Text;

namespace Tack.Core.Phone.Http;

/// <summary>
/// What the phone server answers: a status, a content type, a body (bytes, or
/// a file streamed from disk) and any headers of its own. The headers every
/// response carries (the CSP, nosniff and the rest) are added when it is
/// sent, as is Cache-Control: no-store unless the response sets its own.
/// </summary>
public sealed class PhoneResponse
{
    const string PlainText = "text/plain; charset=utf-8";

    readonly List<KeyValuePair<string, string>> _headers = [];

    PhoneResponse(int status, string contentType, byte[] body, string? filePath, long length)
    {
        (Status, ContentType, Body, FilePath, Length) = (status, contentType, body, filePath, length);
    }

    public int Status { get; }

    public string ContentType { get; }

    /// <summary>The body, when it is not a file.</summary>
    public byte[] Body { get; }

    /// <summary>The file to stream as the body, when there is one.</summary>
    public string? FilePath { get; }

    /// <summary>The body's length in bytes: Content-Length.</summary>
    public long Length { get; }

    /// <summary>Headers of this response's own, in the order added.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers => _headers;

    public static PhoneResponse Bytes(int status, string contentType, byte[] body) =>
        new(status, contentType, body, null, body.LongLength);

    /// <summary>A status with no body.</summary>
    public static PhoneResponse Empty(int status) => Bytes(status, PlainText, []);

    /// <summary>A few words for the page to show, as plain UTF-8 text.</summary>
    public static PhoneResponse Text(int status, string said) => Bytes(status, PlainText, Encoding.UTF8.GetBytes(said));

    /// <summary>A JSON body, already serialized.</summary>
    public static PhoneResponse Json(int status, byte[] json) => Bytes(status, "application/json", json);

    /// <summary>A 200 streaming <paramref name="length"/> bytes of the file at <paramref name="path"/>.</summary>
    public static PhoneResponse File(string contentType, string path, long length) =>
        new(200, contentType, [], path, length);

    /// <summary>Adds a header of its own (ETag, Cache-Control) and returns the same response.</summary>
    public PhoneResponse With(string name, string value)
    {
        _headers.Add(new(name, value));
        return this;
    }

    /// <summary>The value of a header of its own, or null.</summary>
    public string? Header(string name) =>
        _headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}
