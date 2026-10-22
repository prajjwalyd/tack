using Tack.Windows.Clipboard;

namespace Tack.Windows.Tests.Clipboard;

// Registering a format name only asks Windows for its number; nothing here
// opens or changes the clipboard.
public sealed class ClipboardFormatTests
{
    static readonly string[] s_markers =
    [
        "ExcludeClipboardContentFromMonitorProcessing",
        "Clipboard Viewer Ignore",
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard",
    ];

    [Fact]
    public void Gdi_and_private_formats_are_never_restored()
    {
        foreach (uint format in new[] { 2, 3, 9, 14, 0x80, 0x200, 0x2FF, 0x300, 0x3FF })
        {
            Assert.False(ClipboardFormats.Restorable(format), $"0x{format:x}");
        }
        // CF_TEXT, CF_DIB, CF_UNICODETEXT, CF_HDROP, CF_LOCALE, CF_DIBV5.
        foreach (uint format in new[] { 1, 8, 13, 15, 16, 17 })
        {
            Assert.True(ClipboardFormats.Restorable(format), $"{format}");
        }
    }

    [Fact]
    public void Registered_formats_are_restored_unless_they_are_live_ole_objects()
    {
        Assert.True(ClipboardFormats.Restorable(ClipboardFormats.Registered("HTML Format")));
        // A password manager's marker comes back with its content.
        Assert.True(ClipboardFormats.Restorable(ClipboardFormats.Registered("ExcludeClipboardContentFromMonitorProcessing")));
        Assert.False(ClipboardFormats.Restorable(ClipboardFormats.Registered("Ole Private Data")));
        Assert.False(ClipboardFormats.Restorable(ClipboardFormats.Registered("Embed Source")));
    }

    [Fact]
    public void Every_privacy_marker_is_known_and_kept()
    {
        foreach (var name in s_markers)
        {
            var format = ClipboardFormats.Registered(name);
            Assert.True(PrivacyMarkers.IsMarker(format), name);
            Assert.True(ClipboardFormats.Restorable(format), name);
        }
        Assert.False(PrivacyMarkers.IsMarker(ClipboardFormats.Registered("HTML Format")));
    }

    [Fact]
    public void A_marker_counts_when_present_unless_it_allows()
    {
        // The first two mean "do not look" whatever they hold.
        Assert.True(PrivacyMarkers.Counts(0, present: true, [1, 0, 0, 0]));
        Assert.True(PrivacyMarkers.Counts(1, present: true, null));
        // History and cloud count unless they say 1, as an app may set them with no data.
        Assert.True(PrivacyMarkers.Counts(2, present: true, null));
        Assert.True(PrivacyMarkers.Counts(3, present: true, [0, 0, 0, 0]));
        Assert.False(PrivacyMarkers.Counts(2, present: true, [1, 0, 0, 0, 9]));
        Assert.False(PrivacyMarkers.Counts(0, present: false, null));
    }

    [Fact]
    public void Text_copied_from_a_print_gets_windows_line_breaks() =>
        Assert.Equal("one\r\ntwo\r\n\r\nthree", ClipboardWriter.WindowsLineBreaks("one\ntwo\r\n\nthree"));
}
