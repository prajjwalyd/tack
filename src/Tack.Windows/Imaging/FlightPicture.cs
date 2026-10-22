using Tack.Core.Thumbnails;

namespace Tack.Windows.Imaging;

/// <summary>The pictures a new capture brings onto the board.</summary>
/// <param name="Jpeg">What it flies with: a JPEG about the size of the snip, long side at most 1600 px.</param>
/// <param name="Thumb">The print it becomes, with the full picture's size and hash.</param>
public sealed record FlightPicture(byte[] Jpeg, Thumb Thumb);
