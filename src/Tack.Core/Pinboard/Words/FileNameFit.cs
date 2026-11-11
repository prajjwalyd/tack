namespace Tack.Core.Pinboard.Words;

/// <summary>
/// A file's name cut to fit its card: the name whole when it fits, otherwise
/// its start, an ellipsis, and its end with the extension, so both what it
/// is called and what kind of file it is stay in view
/// ("usageReport_1_38…c31.csv"). A name without an extension (a folder) loses
/// its end instead.
/// </summary>
public static class FileNameFit
{
    const string Ellipsis = "…";

    /// <summary>The name's last characters kept before the extension.</summary>
    const int TailChars = 3;

    /// <summary>An extension longer than this (dot included) is taken for part of the name.</summary>
    const int MaxExtension = 8;

    /// <summary>
    /// <paramref name="name"/> as it fits in <paramref name="maxWidth"/>,
    /// measured by <paramref name="width"/>. When not even the ellipsis and
    /// the end fit, that is what comes back, and the line trims the rest.
    /// </summary>
    public static string Fit(string name, double maxWidth, Func<string, double> width)
    {
        if (maxWidth <= 0 || width(name) <= maxWidth)
        {
            return name;
        }
        var tail = Tail(name);
        var stem = name[..^tail.Length];
        if (stem.Length == 0)
        {
            return name;
        }
        // The most of the start that fits: the cut name only grows with it.
        var (lo, hi) = (0, stem.Length - 1);
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (width(Join(stem, mid, tail)) <= maxWidth)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return Join(stem, lo, tail);
    }

    /// <summary>The end kept after the ellipsis: the extension and a few characters before it; none without an extension.</summary>
    static string Tail(string name)
    {
        var dot = name.LastIndexOf('.');
        var extension = dot > 0 ? name[dot..] : "";
        if (extension.Length is < 2 or > MaxExtension || extension.Contains(' '))
        {
            return "";
        }
        var start = Math.Max(1, dot - TailChars);
        // Never between the halves of a character outside the basic plane.
        if (char.IsLowSurrogate(name[start]))
        {
            start--;
        }
        return name[start..];
    }

    static string Join(string stem, int count, string tail)
    {
        if (count > 0 && char.IsHighSurrogate(stem[count - 1]))
        {
            count--;
        }
        return stem[..count].TrimEnd() + Ellipsis + tail;
    }
}
