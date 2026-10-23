using Tack.Windows.Clipboard;

namespace Tack.Windows.Tests.Clipboard;

// Nothing here opens or changes the clipboard.
public sealed class ClipboardFormatTests
{
    [Fact]
    public void Live_ole_objects_are_left_out_of_a_saved_clipboard_and_nothing_else()
    {
        Assert.True(ClipboardFormats.IsLiveObject("Ole Private Data"));
        Assert.True(ClipboardFormats.IsLiveObject("DataObject"));
        Assert.True(ClipboardFormats.IsLiveObject("Embed Source"));
        Assert.False(ClipboardFormats.IsLiveObject("HTML Format"));
        Assert.False(ClipboardFormats.IsLiveObject("PNG"));
        // A password manager's marker comes back with its content.
        Assert.False(ClipboardFormats.IsLiveObject("ExcludeClipboardContentFromMonitorProcessing"));
    }

    [Fact]
    public void A_marker_counts_when_present_unless_it_allows()
    {
        // The first two mean "do not look" whatever they hold.
        Assert.True(PrivacyMarkers.Counts(0, present: true, [1, 0, 0, 0]));
        Assert.True(PrivacyMarkers.Counts(1, present: true, null));
        // History counts unless it says 1, as an app may set it with no data.
        Assert.True(PrivacyMarkers.Counts(2, present: true, null));
        Assert.True(PrivacyMarkers.Counts(2, present: true, [0, 0, 0, 0]));
        Assert.False(PrivacyMarkers.Counts(2, present: true, [1, 0, 0, 0, 9]));
        Assert.False(PrivacyMarkers.Counts(0, present: false, null));
    }

    [Fact]
    public void Every_privacy_marker_is_known() => Assert.Equal(
        ["ExcludeClipboardContentFromMonitorProcessing", "Clipboard Viewer Ignore", "CanIncludeInClipboardHistory"],
        PrivacyMarkers.Names);

    [Fact]
    public void Text_that_is_only_blanks_or_placeholders_is_no_selection()
    {
        Assert.True(ClipboardView.HasContent(" a "));
        Assert.False(ClipboardView.HasContent(" \r\n\t"));
        // What Word and browsers give for a selected picture or object.
        Assert.False(ClipboardView.HasContent("￼\n"));
    }

    [Fact]
    public void Text_copied_from_a_print_gets_windows_line_breaks() =>
        Assert.Equal("one\r\ntwo\r\n\r\nthree", ClipboardWriter.WindowsLineBreaks("one\ntwo\r\n\nthree"));
}
