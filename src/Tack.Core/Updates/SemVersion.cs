using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tack.Core.Updates;

/// <summary>
/// A version as Tack 0.2's updater read one: major.minor.patch, then an
/// optional pre-release after "-" and build after "+", with any leading "v"
/// dropped. A pre-release is below its release ("0.3.0-beta.2" &lt; "0.3.0"),
/// and the dot-separated parts of a pre-release compare as numbers when they
/// are numbers ("beta.2" &lt; "beta.10"). The build only breaks ties, so two
/// versions are equal only when they are written alike.
/// </summary>
public sealed record SemVersion : IComparable<SemVersion>
{
    SemVersion(ulong major, ulong minor, ulong patch, string pre, string build) =>
        (Major, Minor, Patch, Pre, Build) = (major, minor, patch, pre, build);

    public ulong Major { get; }
    public ulong Minor { get; }
    public ulong Patch { get; }

    /// <summary>The pre-release, as "beta.2"; empty for a release.</summary>
    public string Pre { get; }

    /// <summary>The build, as "abc123"; empty for none.</summary>
    public string Build { get; }

    /// <summary>Reads <paramref name="text"/>, or says it is not a version.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        if (text is null)
        {
            return false;
        }
        var rest = text.TrimStart('v');
        var build = string.Empty;
        if (rest.IndexOf('+', StringComparison.Ordinal) is var plus and >= 0)
        {
            build = rest[(plus + 1)..];
            rest = rest[..plus];
            if (!Identifiers(build, numbersStrict: false))
            {
                return false;
            }
        }
        var pre = string.Empty;
        if (rest.IndexOf('-', StringComparison.Ordinal) is var dash and >= 0)
        {
            pre = rest[(dash + 1)..];
            rest = rest[..dash];
            if (!Identifiers(pre, numbersStrict: true))
            {
                return false;
            }
        }
        var parts = rest.Split('.');
        if (parts.Length != 3 || !Number(parts[0], out var major) || !Number(parts[1], out var minor) || !Number(parts[2], out var patch))
        {
            return false;
        }
        version = new SemVersion(major, minor, patch, pre, build);
        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
        {
            return 1;
        }
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
        {
            return core;
        }
        // A release is above any of its pre-releases.
        var (release, otherRelease) = (Pre.Length == 0, other.Pre.Length == 0);
        if (release != otherRelease)
        {
            return release ? 1 : -1;
        }
        var pre = CompareParts(Pre, other.Pre);
        return pre != 0 ? pre : CompareParts(Build, other.Build);
    }

    public static bool operator <(SemVersion left, SemVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemVersion left, SemVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemVersion left, SemVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemVersion left, SemVersion right) => left.CompareTo(right) >= 0;

    /// <summary>As "0.3.1-beta.2+abc", without the "v".</summary>
    public override string ToString()
    {
        var text = $"{Major}.{Minor}.{Patch}";
        if (Pre.Length > 0)
        {
            text += "-" + Pre;
        }
        return Build.Length > 0 ? text + "+" + Build : text;
    }

    /// <summary>
    /// Dot-separated parts compared one by one: numbers as numbers, below any
    /// word; words in ASCII order; with all else equal, fewer parts is lower.
    /// </summary>
    static int CompareParts(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return a.Length.CompareTo(b.Length);
        }
        var (left, right) = (a.Split('.'), b.Split('.'));
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var (x, y) = (left[i], right[i]);
            var (xNumber, yNumber) = (x.All(char.IsAsciiDigit), y.All(char.IsAsciiDigit));
            int order;
            if (xNumber && yNumber)
            {
                // Without leading zeros the longer number is the larger one.
                var (xs, ys) = (x.TrimStart('0'), y.TrimStart('0'));
                order = xs.Length != ys.Length ? xs.Length.CompareTo(ys.Length) : string.CompareOrdinal(xs, ys);
            }
            else
            {
                order = xNumber != yNumber ? (xNumber ? -1 : 1) : string.CompareOrdinal(x, y);
            }
            if (order != 0)
            {
                return Math.Sign(order);
            }
        }
        return left.Length.CompareTo(right.Length);
    }

    /// <summary>A whole number with no sign and no leading zero.</summary>
    static bool Number(string text, out ulong value)
    {
        value = 0;
        return text.Length > 0 && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Dot-separated parts of ASCII letters, digits and hyphens, none empty;
    /// in a pre-release a number part has no leading zero.
    /// </summary>
    static bool Identifiers(string text, bool numbersStrict)
    {
        foreach (var part in text.Split('.'))
        {
            if (part.Length == 0 || !part.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }
            if (numbersStrict && part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit))
            {
                return false;
            }
        }
        return true;
    }
}
