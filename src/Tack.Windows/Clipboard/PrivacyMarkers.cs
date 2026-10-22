namespace Tack.Windows.Clipboard;

/// <summary>
/// The privacy markers apps put beside a copy. Either of the first two means
/// "do not look"; the last two, set to 0, keep it out of clipboard history and
/// the cloud clipboard. A password manager sets them, and Tack honours all
/// three conventions (kept out of the cloud alone is not private).
/// </summary>
static class PrivacyMarkers
{
    public static readonly string[] Names =
    [
        "ExcludeClipboardContentFromMonitorProcessing",
        "Clipboard Viewer Ignore",
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard",
    ];

    /// <summary>Where the history and cloud markers start in <see cref="Names"/>.</summary>
    public const int FirstAllowance = 2;

    static readonly uint[] s_formats = [.. Names.Select(ClipboardFormats.Registered)];

    /// <summary>The markers' format numbers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<uint> Formats => s_formats;

    public static bool IsMarker(uint format) => format != 0 && s_formats.Contains(format);

    /// <summary>
    /// Which markers are on the clipboard, while it is open. Presence is what
    /// counts, as an app may set a marker with no data.
    /// </summary>
    public static bool[] Read(ClipboardSession open)
    {
        var marks = new bool[s_formats.Length];
        for (var i = 0; i < marks.Length; i++)
        {
            var present = ClipboardSession.Available(s_formats[i]);
            marks[i] = Counts(i, present, present && i >= FirstAllowance ? open.Bytes(s_formats[i]) : null);
        }
        return marks;
    }

    /// <summary>
    /// Whether marker <paramref name="index"/> marks the copy: present, and for
    /// history and cloud, not saying 1 (allowed).
    /// </summary>
    public static bool Counts(int index, bool present, byte[]? data) =>
        present && (index < FirstAllowance || data is null || !data.AsSpan().StartsWith((ReadOnlySpan<byte>)[1, 0, 0, 0]));

    /// <summary>Whether marks from <see cref="Read"/> make the copy private.</summary>
    public static bool IsPrivate(bool[] marks) => marks[0] || marks[1] || marks[2];
}
