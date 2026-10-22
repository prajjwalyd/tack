using System.Text;
using Tack.Core.NetBird;
using Tack.Core.Notes;
using Tack.Core.Phone.Http;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// Pins what an allowed device sent: a JPEG or a PNG, or text. Each check
/// comes before the next costs anything: the type and the declared size, the
/// device's pins this minute, a turn among the pins under way, and only then
/// the body. The answer is a few plain words the page shows as they are.
/// </summary>
internal sealed class PinRoute(IPhoneHost host, PinLimits limits)
{
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task<PhoneResponse> HandleAsync(RequestHead head, IRequestBody body, Peer peer)
    {
        var media = AsciiCase.Lower((head.Header("Content-Type") ?? string.Empty).Split(';')[0].Trim());
        if (media is not ("image/jpeg" or "image/png" or "text/plain"))
        {
            return PhoneResponse.Text(415, "Only photos and text can be pinned");
        }
        if (head.ContentLength > PhoneRoutes.MaxPin)
        {
            return PhoneResponse.Text(413, "That's too large to pin");
        }
        if (!limits.MayPin(peer.Key))
        {
            return PhoneResponse.Text(429, "Slow down a little");
        }
        // Held until the pin is done, so at most two bodies are in memory at once.
        using var turn = limits.TryTurn();
        if (turn is null)
        {
            return PhoneResponse.Text(503, "Busy, try again in a moment");
        }
        if (await body.ReadAsync(PhoneRoutes.MaxPin).ConfigureAwait(false) is not { } bytes)
        {
            return PhoneResponse.Text(413, "That didn't arrive in full");
        }
        if (media == "text/plain")
        {
            return await PinTextAsync(bytes).ConfigureAwait(false);
        }
        return await host.PinImageAsync($"photo from {peer.ShortName}", bytes).ConfigureAwait(false)
            ? PhoneResponse.Text(200, "Pinned")
            : PhoneResponse.Text(422, "Couldn't pin that picture");
    }

    async Task<PhoneResponse> PinTextAsync(byte[] bytes)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return PhoneResponse.Text(415, "Text must be UTF-8");
        }
        if (NoteText.Body(text) is not { } note)
        {
            return PhoneResponse.Text(422, "Nothing to pin");
        }
        if (host.HasNote(note.Text))
        {
            return PhoneResponse.Text(409, "Already on your board");
        }
        await host.PinTextAsync(text).ConfigureAwait(false);
        return PhoneResponse.Text(200, "Pinned");
    }
}
