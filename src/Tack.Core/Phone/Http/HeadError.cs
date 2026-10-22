namespace Tack.Core.Phone.Http;

/// <summary>Why a request head could not be read.</summary>
public enum HeadError
{
    /// <summary>The client went away or stalled: nothing is answered.</summary>
    Gone,

    /// <summary>Over 16 KiB of head, or over 32 headers: 431.</summary>
    HeadTooLarge,

    /// <summary>Not HTTP, a header that is not UTF-8, or a bad Content-Length: 400.</summary>
    Malformed,

    /// <summary>A chunked body, which the page never sends: 411.</summary>
    LengthRequired,
}
