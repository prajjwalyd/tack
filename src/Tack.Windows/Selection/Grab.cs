using Tack.Windows.Clipboard;

namespace Tack.Windows.Selection;

/// <summary>What pinning the selection found, and what became of the user's clipboard.</summary>
/// <param name="What">
/// The selection: text, a picture or files; private when the app marked its
/// copy so (a password manager); empty when nothing was copied (no selection,
/// an app that does not copy on Ctrl+C, or one running as administrator, which
/// Tack cannot send keys to).
/// </param>
/// <param name="Clipboard">What became of the user's clipboard.</param>
/// <param name="KeysHeld">The shortcut's keys were held down too long; nothing was sent.</param>
public sealed record Grab(ClipboardContent What, ClipboardFate Clipboard, bool KeysHeld = false)
{
    internal static Grab Nothing { get; } = new(ClipboardContent.Empty, ClipboardFate.Untouched);
}
