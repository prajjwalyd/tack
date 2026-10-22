using Tack.Windows.Imaging;

namespace Tack.Windows.Clipboard;

/// <summary>What an app put on the clipboard, as Tack would pin it.</summary>
public abstract record ClipboardContent
{
    /// <summary>Marked private (a password manager's copy): never pinned.</summary>
    public static ClipboardContent Private { get; } = new PrivateContent();

    /// <summary>Nothing Tack can pin.</summary>
    public static ClipboardContent Empty { get; } = new EmptyContent();

    ClipboardContent()
    {
    }

    /// <summary>Text with something in it besides whitespace and control characters.</summary>
    public sealed record TextContent(string Text) : ClipboardContent;

    /// <summary>A picture.</summary>
    public sealed record PictureContent(Picture Picture) : ClipboardContent;

    /// <summary>Files copied in Explorer (or any app that copies files), by full path.</summary>
    public sealed record FilesContent(IReadOnlyList<string> Paths) : ClipboardContent;

    /// <summary>See <see cref="Private"/>.</summary>
    public sealed record PrivateContent : ClipboardContent;

    /// <summary>See <see cref="Empty"/>.</summary>
    public sealed record EmptyContent : ClipboardContent;
}
