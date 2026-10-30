using Tack.Core.Pinboard.Words;
using Tack.Core.Prints;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// One print as the phone page reads it. A picture is fetched by address and
/// kept by the phone, so its addresses carry a digest of its thumbnail
/// (<c>?v=</c>): an edited picture gets a new address. A linked file shows
/// its thumbnail, name, type and size, and a file (not a folder) can be
/// downloaded from its own address.
/// </summary>
/// <param name="Note">True for a note, false for a picture or a file.</param>
/// <param name="Thumb">The thumbnail's address; "" for a note, or a file with none.</param>
/// <param name="Full">The full picture's address; "" for a note or a file.</param>
/// <param name="Text">A note's text; null otherwise.</param>
/// <param name="Link">A link note's URL; null otherwise.</param>
/// <param name="File">True for a linked file or folder.</param>
/// <param name="Name">A linked file's name; null otherwise.</param>
/// <param name="Type">A linked file's type in Windows' words, or "Folder"; null otherwise.</param>
/// <param name="Size">A linked file's size in words ("2 MB"); "" for a folder, null otherwise.</param>
/// <param name="Download">Where a linked file downloads from; "" for a folder or anything else.</param>
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
    bool Kept,
    bool File,
    string? Name,
    string? Type,
    string? Size,
    string Download)
{
    public static PrintView Of(Print print)
    {
        var picture = print.Kind == Kind.Image;
        var thumbed = print.Kind != Kind.Note && print.Thumb is not null;
        var v = Fnv1a.Hex(print.Thumb?.Jpeg ?? []);
        var file = print.IsFile ? print.File ?? new FileFacts(false, 0, string.Empty) : null;
        return new PrintView(
            print.Id,
            print.IsNote,
            thumbed ? $"/api/thumb/{print.Id}?v={v}" : string.Empty,
            picture ? $"/api/print/{print.Id}?v={v}" : string.Empty,
            print.Width,
            print.Height,
            print.Note?.Text,
            print.Note?.Link,
            print.PinnedAt,
            print.Kept,
            file is not null,
            file is null ? null : print.Name,
            file is null ? null : file.Folder ? "Folder" : file.Type,
            file is null ? null : file.Folder ? string.Empty : FileSize.Words(file.Size),
            file is { Folder: false } ? $"/api/file/{print.Id}" : string.Empty);
    }
}
