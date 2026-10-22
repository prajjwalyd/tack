namespace Tack.Core.Files;

/// <summary>Moves files without ever replacing one that is already there.</summary>
public static class FileMove
{
    /// <summary>Far more names than any real folder needs; past it, something is wrong.</summary>
    const int MaxNames = 10_000;

    // ERROR_FILE_EXISTS and ERROR_ALREADY_EXISTS, as Windows reports a taken name.
    const int FileExists = unchecked((int)0x80070050);
    const int AlreadyExists = unchecked((int)0x800700B7);

    /// <summary>
    /// Moves <paramref name="from"/> into <paramref name="folder"/> under its own
    /// name, or "name (2).png" and so on, never over a file that is already
    /// there, even one that turns up while the move runs. <paramref name="claim"/>
    /// hears each name just before it is tried and may call the move off by
    /// returning false. Returns where the file went, or null if it was called off.
    /// </summary>
    public static string? ToFreeName(string from, string folder, Func<string, bool> claim)
    {
        var stem = Path.GetFileNameWithoutExtension(from);
        var extension = Path.GetExtension(from) is { Length: > 0 } dotted ? dotted[1..] : "png";
        for (var n = 1; n <= MaxNames; n++)
        {
            var to = Path.Combine(folder, n == 1 ? $"{stem}.{extension}" : $"{stem} ({n}).{extension}");
            if (Path.Exists(to))
            {
                continue;
            }
            if (!claim(to))
            {
                return null;
            }
            try
            {
                ToNewName(from, to);
                return to;
            }
            catch (FileExistsException)
            {
                // Taken since the look above: on to the next name.
            }
        }
        throw new IOException($"No free name for {stem}.{extension}.");
    }

    /// <summary>
    /// Moves <paramref name="from"/> to <paramref name="to"/>, throwing
    /// <see cref="FileExistsException"/> rather than replace anything there.
    /// Windows refuses a taken name in the same step as the move, so another
    /// app cannot slip a file in between.
    /// </summary>
    public static void ToNewName(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: false);
        }
        catch (IOException e) when (IsTaken(e))
        {
            throw new FileExistsException(to, e);
        }
    }

    /// <summary>The error Windows gives when a new file's name is already taken.</summary>
    internal static bool IsTaken(IOException e) => e.HResult is FileExists or AlreadyExists;
}
