using Tack.Core.Phone.Http;
using Tack.Core.Prints;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// The board for an allowed device, tagged with a digest of its JSON, so a
/// page that already shows it is told 304 and nothing more.
/// </summary>
internal static class BoardResponse
{
    /// <param name="prints">The board's prints, in its order.</param>
    /// <param name="pc">This PC's short NetBird name.</param>
    /// <param name="you">The device's short NetBird name.</param>
    /// <param name="ifNoneMatch">The page's If-None-Match: the ETag of the board it shows, if any.</param>
    public static PhoneResponse Of(IReadOnlyList<Print> prints, string pc, string you, string? ifNoneMatch)
    {
        var body = PhoneJson.Of(new BoardView(pc, you, [.. prints.Select(PrintView.Of)]));
        var tag = $"\"{Fnv1a.Hex(body)}\"";
        if (ifNoneMatch == tag)
        {
            return PhoneResponse.Empty(304).With("ETag", tag);
        }
        return PhoneResponse.Json(200, body).With("ETag", tag);
    }
}
