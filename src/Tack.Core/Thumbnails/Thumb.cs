namespace Tack.Core.Thumbnails;

/// <summary>A picture's thumbnail and what it was made from.</summary>
/// <param name="Jpeg">The thumbnail as JPEG, long side at most 360 px.</param>
/// <param name="Width">The original picture's width in pixels.</param>
/// <param name="Height">The original picture's height in pixels.</param>
/// <param name="Hash">
/// The original's pixel hash, when it was taken from the full picture. Two
/// prints are only ever one picture if theirs are known and match.
/// </param>
public sealed record Thumb(byte[] Jpeg, int Width, int Height, ulong? Hash);
