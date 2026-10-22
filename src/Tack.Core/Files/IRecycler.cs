namespace Tack.Core.Files;

/// <summary>
/// Somewhere files go instead of being deleted for good: the Recycle Bin. Tack
/// never destroys a file outright. Behind this interface so the cleanup rules
/// can be tested without the real bin.
/// </summary>
public interface IRecycler
{
    /// <summary>Moves the file there. Throws an IOException on failure, leaving the file exactly where it was.</summary>
    void Recycle(string path);
}
