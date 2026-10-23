namespace Tack.Core.Updates;

/// <summary>
/// The rule for following redirects by hand: GitHub sends a release's files
/// on to its download servers, and every hop must be https, at most
/// <see cref="MaxRedirects"/> of them. A redirect that breaks the rule is the
/// server's failure.
/// </summary>
public static class HttpsRedirects
{
    /// <summary>The most redirects followed for one request.</summary>
    public const int MaxRedirects = 5;

    /// <summary>An absolute https address.</summary>
    public static bool IsHttps(Uri url) => url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps;

    /// <summary>A status that sends the request elsewhere: 301, 302, 303, 307 or 308.</summary>
    public static bool IsRedirect(int status) => status is 301 or 302 or 303 or 307 or 308;

    /// <summary>
    /// Where the request goes after the server at <paramref name="from"/> sent
    /// it to <paramref name="location"/> (which may be relative), when
    /// <paramref name="followed"/> redirects have been followed already.
    /// </summary>
    /// <exception cref="UpdateFailureException">No location, one that is not https, or one redirect too many.</exception>
    public static Uri Next(Uri from, Uri? location, int followed)
    {
        if (followed >= MaxRedirects)
        {
            throw new UpdateFailureException(UpdateFailure.Server, $"more than {MaxRedirects} redirects");
        }
        if (location is null)
        {
            throw new UpdateFailureException(UpdateFailure.Server, "a redirect without a location");
        }
        var next = location.IsAbsoluteUri ? location : new Uri(from, location);
        return IsHttps(next) ? next : throw new UpdateFailureException(UpdateFailure.Server, "a redirect that is not https");
    }
}
