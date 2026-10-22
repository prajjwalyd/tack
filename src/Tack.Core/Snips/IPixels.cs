namespace Tack.Core.Snips;

/// <summary>Screen pixels, by absolute physical position: a grab of the screen, or a made-up one in tests.</summary>
public interface IPixels
{
    /// <summary>The pixel at (x, y), or null off the grab.</summary>
    Rgb? Get(int x, int y);
}
