namespace Tack.Core.Phone.Serving;

/// <summary>The phone page's own files, served unchanged to any NetBird peer: they hold nothing of the board.</summary>
/// <param name="Page">index.html.</param>
/// <param name="Script">phone.js.</param>
/// <param name="Style">phone.css.</param>
/// <param name="Icon">icon.png, 256 × 256, for "Add to Home screen".</param>
public sealed record PhoneFiles(byte[] Page, byte[] Script, byte[] Style, byte[] Icon);
