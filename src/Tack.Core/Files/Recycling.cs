using Tack.Core.Diagnostics;

namespace Tack.Core.Files;

/// <summary>Sends Tack's own files to the Recycle Bin, never deleting them for good.</summary>
public static class Recycling
{
    /// <summary>
    /// Recycles one file if it still exists. A failure is logged and the file
    /// stays put: there is no fallback to deleting it. Returns whether it went.
    /// </summary>
    public static bool RecycleFile(string path, IRecycler bin)
    {
        if (!Path.Exists(path))
        {
            return false;
        }
        try
        {
            bin.Recycle(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"cannot move {Path.GetFileName(path)} to the Recycle Bin, leaving it in place: {e.Message}");
            return false;
        }
    }
}
