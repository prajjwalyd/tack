using System.Security;
using Tack.Core.Phone.Http;
using Tack.Core.Prints;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// A picture print's thumbnail and its full picture, for an allowed device.
/// Both are addressed by content (<c>?v=</c>), so the phone may keep them.
/// Anything that is not a picture on the board is 404.
/// </summary>
internal static class PictureResponse
{
    /// <summary>Lets the phone keep a picture: its address changes when it does.</summary>
    public const string Keep = "private, max-age=31536000, immutable";

    /// <summary>The thumbnail, as the JPEG the board shows.</summary>
    public static PhoneResponse Thumb(Print? print) =>
        print is { Kind: Kind.Image, Thumb: { } thumb }
            ? PhoneResponse.Bytes(200, "image/jpeg", thumb.Jpeg).With("Cache-Control", Keep)
            : PhoneResponse.Empty(404);

    /// <summary>
    /// The full picture, streamed from its file once a capture from a moment
    /// ago is on disk. A missing or empty file, or one over
    /// <see cref="PhoneRoutes.MaxServed"/>, is 404.
    /// </summary>
    public static async Task<PhoneResponse> FullAsync(IPhoneHost host, Print? print)
    {
        if (print is not { Kind: Kind.Image })
        {
            return PhoneResponse.Empty(404);
        }
        await host.WaitSavedAsync(print.Path).ConfigureAwait(false);
        var length = LengthOf(print.Path);
        if (length is <= 0 or > PhoneRoutes.MaxServed)
        {
            return PhoneResponse.Empty(404);
        }
        var png = AsciiCase.Same(Path.GetExtension(print.Path), ".png");
        return PhoneResponse.File(png ? "image/png" : "image/jpeg", print.Path, length)
            .With("Cache-Control", Keep);
    }

    /// <summary>The file's length in bytes; 0 when it is not there or cannot be looked at.</summary>
    static long LengthOf(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or SecurityException)
        {
            return 0;
        }
    }
}
