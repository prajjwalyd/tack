using System.Globalization;

namespace Tack.Core.Pinboard.Words;

/// <summary>A file's size in short words, as Explorer counts it (1 KB is 1024 bytes): "512 bytes", "1.5 MB", "24 MB".</summary>
public static class FileSize
{
    static readonly string[] s_units = ["KB", "MB", "GB", "TB"];

    public static string Words(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        if (bytes < 1024)
        {
            return bytes == 1 ? "1 byte" : $"{Math.Max(bytes, 0).ToString(culture)} bytes";
        }
        var size = bytes / 1024.0;
        var unit = 0;
        while (size >= 1024 && unit < s_units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        // One decimal under 10 ("1.5 MB"), none above ("24 MB"); never "10.0".
        var rounded = Math.Round(size, size < 10 ? 1 : 0, MidpointRounding.AwayFromZero);
        return $"{rounded.ToString(rounded < 10 ? "0.#" : "0", culture)} {s_units[unit]}";
    }
}
