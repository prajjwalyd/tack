using Tack.Core.Files;

namespace Tack.Core.Captures;

/// <summary>
/// Writing Tack's own files in two steps: claim a free name by creating its
/// empty ".part" file (fast, so a capture can be pinned before its picture is
/// written), then write the bytes and move them into place. Until the move the
/// file exists only as a ".part", which nothing mistakes for a picture or a note.
/// </summary>
public static class ReservedFiles
{
    const string Partial = ".part";

    /// <summary>Where a file is written before it is moved to <paramref name="path"/>.</summary>
    public static string PartialPath(string path) => path + Partial;

    public static bool IsPartial(string path) => Path.GetFileName(path).EndsWith(Partial, StringComparison.Ordinal);

    /// <summary>Claims "Screenshot YYYY-MM-DD HHMMSS.png" in <paramref name="folder"/>, or " (2)" and so on if taken.</summary>
    public static string Reserve(string folder, Timestamp at) => ReserveAs(folder, at.FileStem(), "png");

    /// <summary>Claims "&lt;stem&gt;.&lt;extension&gt;" in <paramref name="folder"/>, or "&lt;stem&gt; (2).&lt;extension&gt;" and so on if taken.</summary>
    public static string ReserveAs(string folder, string stem, string extension)
    {
        Directory.CreateDirectory(folder);
        for (var n = 1; ; n++)
        {
            var path = Path.Combine(folder, n == 1 ? $"{stem}.{extension}" : $"{stem} ({n}).{extension}");
            if (Path.Exists(path))
            {
                continue;
            }
            try
            {
                using (new FileStream(PartialPath(path), FileMode.CreateNew, FileAccess.Write))
                {
                }
                return path;
            }
            catch (IOException e) when (FileMove.IsTaken(e))
            {
                // Claimed by another writer meanwhile: on to the next name.
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> into the ".part" file reserved for
    /// <paramref name="path"/>, flushes them to disk, then moves the file into
    /// place. On failure the ".part" file is removed.
    /// </summary>
    public static void WriteReserved(ReadOnlySpan<byte> bytes, string path)
    {
        var partial = PartialPath(path);
        try
        {
            using (var stream = new FileStream(partial, FileMode.Truncate, FileAccess.Write))
            {
                stream.Write(bytes);
                // On disk before its name says it is whole: a crash just after
                // the move must not leave an empty or half-written file.
                stream.Flush(flushToDisk: true);
            }
            File.Move(partial, path, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>Saves bytes as they are (a picture dropped on the board) as "&lt;stem&gt; YYYY-MM-DD HHMMSS.&lt;extension&gt;".</summary>
    public static string SaveBytes(ReadOnlySpan<byte> bytes, string folder, string stem, string extension, Timestamp at)
    {
        var path = ReserveAs(folder, $"{stem} {at.Stamp()}", extension);
        WriteReserved(bytes, path);
        return path;
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the startup sweep, which takes stray ".part" files.
        }
    }
}
