namespace Tack.Windows.Clipboard;

/// <summary>How a picture copied off the clipboard is encoded.</summary>
public enum CopiedFormat
{
    /// <summary>The registered "PNG" format: a whole PNG file.</summary>
    Png,

    /// <summary>CF_DIB: a bitmap header and its pixels, with no file header.</summary>
    Dib,
}
