using System.Text;
using Tack.Core.Captures;
using Tack.Core.Files;

namespace Tack.Core.Notes;

/// <summary>
/// A note's file: UTF-8 text in Tack's Notes folder, so it can be dragged out,
/// opened and edited like any file. board.json holds only its path.
/// </summary>
public static class NoteFiles
{
    /// <summary>The most of a note's file that is read: a full note after any normalising, and no more.</summary>
    const int MaxReadBytes = 64 * 1024;

    public static bool IsNoteFile(string path) => Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A note's file where Tack keeps them: a .txt directly in <paramref name="notesFolder"/>.
    /// A note anywhere else was not written by Tack and is not restored.
    /// </summary>
    public static bool IsIn(string path, string notesFolder) =>
        PathRules.IsPlain(path)
        && IsNoteFile(path)
        && Path.GetDirectoryName(path) is { } folder
        && CanonicalPath.SameDirectory(folder, notesFolder);

    /// <summary>
    /// Saves a note as "Note YYYY-MM-DD HHMMSS.txt" in <paramref name="folder"/>,
    /// " (2)" and so on if taken: UTF-8 without a byte order mark, Windows line
    /// breaks, written whole to a ".part" file first.
    /// </summary>
    public static string Save(NoteBody body, string folder, Timestamp at)
    {
        var path = ReservedFiles.ReserveAs(folder, $"Note {at.Stamp()}", "txt");
        ReservedFiles.WriteReserved(Encoding.UTF8.GetBytes(body.Text.Replace("\n", "\r\n")), path);
        return path;
    }

    /// <summary>
    /// Reads a note's file back, as far as a note goes. An emptied file is still
    /// a note. The file is shared for writing and deleting while it is read, so
    /// a note open in an editor still reads.
    /// </summary>
    public static NoteBody Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[MaxReadBytes];
        var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return FromBytes(buffer.AsSpan(0, length));
    }

    /// <summary>A byte order mark goes, invalid UTF-8 is replaced, and the text is normalised and clamped as when it was pinned.</summary>
    public static NoteBody FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }
        return NoteText.Body(Encoding.UTF8.GetString(bytes)) ?? new NoteBody(string.Empty, null, null, false);
    }

    /// <summary>Recycles leftover notes by the startup sweep's rules: text files no path in <paramref name="keep"/> names.</summary>
    public static List<string> Sweep(string folder, IReadOnlyList<string> keep, DateTime cutoffUtc, IRecycler bin) =>
        Captures.Sweep.Where(folder, keep, cutoffUtc, bin, IsNoteFile);
}
