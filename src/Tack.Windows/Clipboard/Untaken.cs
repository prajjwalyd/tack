namespace Tack.Windows.Clipboard;

/// <summary>Why a capture was not taken off the clipboard.</summary>
public enum Untaken
{
    /// <summary>Another app held the clipboard open throughout.</summary>
    Busy,

    /// <summary>Something else reached the clipboard after the capture.</summary>
    Replaced,

    /// <summary>Marked private by whoever copied it.</summary>
    Private,

    /// <summary>Its app gave no picture.</summary>
    Unreadable,
}
