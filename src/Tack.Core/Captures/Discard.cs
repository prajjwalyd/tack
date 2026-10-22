using Tack.Core.Files;
using Tack.Core.Prints;

namespace Tack.Core.Captures;

/// <summary>Recycles Tack's own files once nothing needs them. A screenshot file of the user's is never touched.</summary>
public static class Discard
{
    /// <summary>Recycles a print's file once it has left the board, if Tack wrote it and it lies in <paramref name="folder"/>.</summary>
    public static void File(Print print, string folder, IRecycler bin)
    {
        if (print.Owned)
        {
            IfIn(print.Path, folder, bin);
        }
    }

    /// <summary>Recycles a file Tack wrote and no longer needs, if it lies in one of Tack's own folders.</summary>
    public static void Owned(string path, IEnumerable<string> folders, IRecycler bin)
    {
        foreach (var folder in folders)
        {
            IfIn(path, folder, bin);
        }
    }

    /// <summary>Recycles <paramref name="path"/> if it is a file directly in <paramref name="folder"/>, named plainly (no ".." that could lead out).</summary>
    static void IfIn(string path, string folder, IRecycler bin)
    {
        if (PathRules.IsPlain(path)
            && Path.GetDirectoryName(path) is { } parent
            && PathRules.Key(parent) == PathRules.Key(folder)
            && System.IO.File.Exists(path))
        {
            Recycling.RecycleFile(path, bin);
        }
    }
}
