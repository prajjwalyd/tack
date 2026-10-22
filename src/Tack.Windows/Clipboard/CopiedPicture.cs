using Tack.Windows.Imaging;

namespace Tack.Windows.Clipboard;

/// <summary>
/// A picture as copied off the clipboard, still encoded: it is decoded once
/// the clipboard is closed again, so no other app waits on Tack meanwhile.
/// </summary>
public sealed record CopiedPicture(CopiedFormat Format, byte[] Bytes)
{
    /// <summary>
    /// Its pixels, or null if it does not decode. An alpha that is the same
    /// everywhere says nothing, so the picture comes back opaque.
    /// </summary>
    public async Task<Picture?> DecodeAsync()
    {
        try
        {
            var picture = Format == CopiedFormat.Png
                ? await PictureDecoder.DecodeAsync(Bytes).ConfigureAwait(false)
                : await PictureDecoder.DecodeDibAsync(Bytes).ConfigureAwait(false);
            return picture.WithoutFlatAlpha();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
