namespace Tack.Core.Phone.Http;

/// <summary>
/// A request's body, read only when a route wants it, never past a cap. The
/// server's connection implements it with its timeouts; tests hand in bytes.
/// </summary>
public interface IRequestBody
{
    /// <summary>
    /// The whole body; null when Content-Length is over <paramref name="max"/>,
    /// or the body does not arrive in full and in time.
    /// </summary>
    Task<byte[]?> ReadAsync(long max);
}
