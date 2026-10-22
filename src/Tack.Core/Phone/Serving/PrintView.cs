using Tack.Core.Prints;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// One print as the phone page reads it. A picture is fetched by address and
/// kept by the phone, so its addresses carry a digest of its thumbnail
/// (<c>?v=</c>): an edited picture gets a new address.
/// </summary>
/// <param name="Note">True for a note, false for a picture.</param>
/// <param name="Thumb">The thumbnail's address; "" for a note.</param>
/// <param name="Full">The full picture's address; "" for a note.</param>
/// <param name="Text">A note's text; null for a picture.</param>
/// <param name="Link">A link note's URL; null otherwise.</param>
internal sealed record PrintView(
    string Id,
    bool Note,
    string Thumb,
    string Full,
    int Width,
    int Height,
    string? Text,
    string? Link,
    long PinnedAt,
    bool Kept)
{
    public static PrintView Of(Print print)
    {
        var picture = print.Kind == Kind.Image;
        var v = Fnv1a.Hex(print.Thumb?.Jpeg ?? []);
        return new PrintView(
            print.Id,
            !picture,
            picture ? $"/api/thumb/{print.Id}?v={v}" : string.Empty,
            picture ? $"/api/print/{print.Id}?v={v}" : string.Empty,
            print.Width,
            print.Height,
            print.Note?.Text,
            print.Note?.Link,
            print.PinnedAt,
            print.Kept);
    }
}
