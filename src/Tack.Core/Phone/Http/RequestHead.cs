namespace Tack.Core.Phone.Http;

/// <summary>
/// A request's head as the phone server read it, and whatever of its body
/// arrived in the same reads. Header names compare without regard to case;
/// values are trimmed.
/// </summary>
/// <param name="Method">"GET", "POST", as sent.</param>
/// <param name="Path">The target without its query: "/api/thumb/42" for "/api/thumb/42?v=…".</param>
/// <param name="Headers">Every header in the order sent.</param>
/// <param name="ContentLength">The body's length from Content-Length; 0 without one.</param>
/// <param name="EarlyBody">Bytes after the head that came with it: the start of the body.</param>
public sealed record RequestHead(
    string Method, string Path, IReadOnlyList<KeyValuePair<string, string>> Headers, long ContentLength, byte[] EarlyBody)
{
    /// <summary>The first header called <paramref name="name"/>, or null.</summary>
    public string? Header(string name)
    {
        foreach (var (key, value) in Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }
        return null;
    }
}
