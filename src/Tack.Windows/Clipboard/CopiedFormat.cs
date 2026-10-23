namespace Tack.Windows.Clipboard;

/// <summary>How a picture copied off the clipboard is encoded.</summary>
public enum CopiedFormat
{
    /// <summary>The registered "PNG" format: a whole PNG file.</summary>
    Png,

    /// <summary>Any other bitmap, which Windows hands over as a .bmp file.</summary>
    Bitmap,
}
