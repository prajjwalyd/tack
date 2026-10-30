using System.Security;
using System.Text;
using Tack.Core.Phone.Http;
using Tack.Core.Prints;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// A linked file on the board, downloaded by an allowed device: streamed from
/// its place on disk, as an attachment under its own name. Only a file print
/// on the board is ever served, by its id; a folder, a file that is gone, or
/// anything else is 404.
/// </summary>
internal static class FileResponse
{
    public static PhoneResponse Download(Print? print)
    {
        if (print is not { Kind: Kind.File, File.Folder: false })
        {
            return PhoneResponse.Empty(404);
        }
        long length;
        try
        {
            var file = new FileInfo(print.Path);
            if (!file.Exists)
            {
                return PhoneResponse.Empty(404);
            }
            length = file.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or SecurityException)
        {
            return PhoneResponse.Empty(404);
        }
        return PhoneResponse.File("application/octet-stream", print.Path, length)
            .With("Content-Disposition", Disposition(print.Name));
    }

    /// <summary>
    /// "attachment" with the name twice: plain ASCII for old browsers (anything
    /// else, quotes and backslashes too, as "_"), and in full as RFC 5987's
    /// UTF-8 form, which every phone browser reads.
    /// </summary>
    internal static string Disposition(string name)
    {
        if (name.Length == 0)
        {
            name = "file";
        }
        var plain = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            plain.Append(c is >= ' ' and <= '~' and not '"' and not '\\' ? c : '_');
        }
        var encoded = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(name))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || "!#$&+-.^_`|~".Contains(c))
            {
                encoded.Append(c);
            }
            else
            {
                encoded.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return $"attachment; filename=\"{plain}\"; filename*=UTF-8''{encoded}";
    }
}
