using System.Text;

namespace Tack.App.Pinboard.Prints;

/// <summary>A web address as a link note shows it.</summary>
static class Address
{
    const char Break = '​';

    /// <summary>The address with no "http://" or "https://" and no trailing slash.</summary>
    public static string Bare(string url)
    {
        var bare = url;
        foreach (var scheme in (ReadOnlySpan<string>)["https://", "http://"])
        {
            if (bare.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                bare = bare[scheme.Length..];
                break;
            }
        }
        return bare.EndsWith('/') ? bare[..^1] : bare;
    }

    /// <summary>
    /// The address with a chance to wrap after each / ? &amp; = # and before
    /// each dot (a zero-width space), so a long one breaks between its parts.
    /// </summary>
    public static string WithBreaks(string address)
    {
        var text = new StringBuilder(address.Length + 16);
        for (var i = 0; i < address.Length; i++)
        {
            if (i > 0 && (address[i] == '.' || "/?&=#".Contains(address[i - 1])))
            {
                text.Append(Break);
            }
            text.Append(address[i]);
        }
        return text.ToString();
    }
}
