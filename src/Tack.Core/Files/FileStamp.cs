namespace Tack.Core.Files;

/// <summary>A file's modification time and size, to tell a real edit from a repeated event.</summary>
public readonly record struct FileStamp(DateTime ModifiedUtc, long Length)
{
    /// <summary>The file's stamp now, or null if it is not there or cannot be read.</summary>
    public static FileStamp? Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
