namespace Tack.Core.Settings;

/// <summary>
/// Where a link into Settings goes: "phone" is the phone page, any other id a
/// group of the list (which ignores one it does not have), nothing the top of
/// the list. A leading "#" and escapes such as "%73" are allowed, as in a link.
/// </summary>
/// <param name="Phone">The phone page, rather than the list.</param>
/// <param name="Section">The group of the list to scroll to; null for none.</param>
public sealed record SettingsRoute(bool Phone, string? Section)
{
    /// <summary>The id of the phone page.</summary>
    public const string PhoneId = "phone";

    /// <summary>The route an id ("phone", "shortcuts", "#updates", "" or null) names.</summary>
    public static SettingsRoute Of(string? id)
    {
        var text = Decode((id ?? "").TrimStart('#'));
        return text == PhoneId ? new SettingsRoute(true, null) : new SettingsRoute(false, text.Length > 0 ? text : null);
    }

    /// <summary>A link is typed by people too: a bad escape is just an id nothing has.</summary>
    static string Decode(string text)
    {
        try
        {
            var decoded = Uri.UnescapeDataString(text);
            return decoded.Contains('�', StringComparison.Ordinal) ? text : decoded;
        }
        catch (UriFormatException)
        {
            return text;
        }
    }
}
