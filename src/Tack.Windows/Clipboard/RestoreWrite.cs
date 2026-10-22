namespace Tack.Windows.Clipboard;

/// <summary>
/// One format a restore puts on the clipboard, and whether it counts towards
/// "every format put back" (the markers added for privacy's sake do not).
/// </summary>
readonly record struct RestoreWrite(uint Format, byte[] Bytes, bool Counts);
