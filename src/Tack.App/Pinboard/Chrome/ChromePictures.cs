using Microsoft.UI.Xaml.Media;
using Tack.Core.Diagnostics;

namespace Tack.App.Pinboard.Chrome;

/// <summary>The chrome's pictures (the felt's wool, the frame's grain), each loaded once from the app's Assets folder.</summary>
static class ChromePictures
{
    static readonly Dictionary<string, LoadedImageSurface?> Loaded = [];

    /// <summary>
    /// The picture at <paramref name="path"/> under Assets, decoding in the
    /// background; it draws nothing until decoded. Null if it is missing, so
    /// the board goes without it.
    /// </summary>
    public static LoadedImageSurface? Get(string path)
    {
        if (!Loaded.TryGetValue(path, out var surface))
        {
            surface = Load(path);
            Loaded[path] = surface;
        }
        return surface;
    }

    static LoadedImageSurface? Load(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", path));
            var stream = new MemoryStream(bytes).AsRandomAccessStream();
            var surface = LoadedImageSurface.StartLoadFromStream(stream);
            surface.LoadCompleted += (_, e) =>
            {
                // Held by this handler until the picture is decoded.
                stream.Dispose();
                if (e.Status != LoadedImageSourceLoadStatus.Success)
                {
                    DevTrace.Write($"board: {path} did not decode: {e.Status}");
                }
            };
            return surface;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"board: {path} is missing: {e.Message}");
            return null;
        }
    }
}
