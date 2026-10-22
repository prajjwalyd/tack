using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Tack.Core.Diagnostics;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// The pictures prints are drawn with, in Assets\Board (made by
/// scripts\board-assets\render.ps1): the shadows, the button and badge rims,
/// the pins and the paper's fibre. Each but the fibre is baked at 100, 150,
/// 200 and 250 percent, light and dark where the colours differ, and is read
/// once and shared. UI thread only.
/// </summary>
static class PrintArt
{
    static readonly double[] Scales = [1, 1.5, 2, 2.5];
    static readonly Dictionary<string, LoadedImageSurface?> Surfaces = [];
    static BitmapImage? _fibre;

    /// <summary>The paper's fibre, a 256 px square of the fine tile, drawn as it is (the board has no tiling brush).</summary>
    public static BitmapImage? Fibre
    {
        get
        {
            if (_fibre is null && Read("fibre.png") is { } bytes)
            {
                _fibre = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = 256 };
                _fibre.SetSource(new MemoryStream(bytes).AsRandomAccessStream());
            }
            return _fibre;
        }
    }

    /// <summary>The baked scale to draw at a screen <paramref name="scale"/>: the nearest at or above it, else the largest.</summary>
    public static double ScaleFor(double scale)
    {
        foreach (var baked in Scales)
        {
            if (baked >= scale - 0.01)
            {
                return baked;
            }
        }
        return Scales[^1];
    }

    /// <summary>A picture's name in a theme: "rest" is "rest-light" or "rest-dark".</summary>
    public static string Themed(string name, bool dark) => $"{name}-{(dark ? "dark" : "light")}";

    /// <summary>The picture <paramref name="name"/> baked at <paramref name="scale"/> (one of the baked scales); null if it cannot be read.</summary>
    public static LoadedImageSurface? Surface(string name, double scale)
    {
        var file = $"{name}-{(int)Math.Round(scale * 100)}.png";
        if (!Surfaces.TryGetValue(file, out var surface))
        {
            surface = Read(file) is { } bytes
                ? LoadedImageSurface.StartLoadFromStream(new MemoryStream(bytes).AsRandomAccessStream())
                : null;
            Surfaces[file] = surface;
        }
        return surface;
    }

    static byte[]? Read(string file)
    {
        try
        {
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Board", file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"print art: {file} could not be read: {e.Message}");
            return null;
        }
    }
}
