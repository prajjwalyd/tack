using System.Text;
using System.Text.Json;

namespace Tack.Core.Storage;

/// <summary>
/// Every path that board.json and its board.json.bad-* backups mention, read as
/// loosely as can be: each string in them, so even a backup that is no longer
/// valid JSON still protects the files it names from the startup sweep.
/// </summary>
public static class MentionedPaths
{
    /// <summary>
    /// The strings in <paramref name="boardFile"/> and its backups; empty if their
    /// folder does not exist, null if any of them cannot be read: then nobody
    /// can say which files are spoken for.
    /// </summary>
    public static List<string>? In(string boardFile)
    {
        var folder = Path.GetDirectoryName(boardFile);
        var name = Path.GetFileName(boardFile).ToLowerInvariant();
        if (string.IsNullOrEmpty(folder) || name.Length == 0)
        {
            return null;
        }
        var backupPrefix = name + ".bad-";
        try
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }
            var paths = new List<string>();
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                var entryName = Path.GetFileName(entry).ToLowerInvariant();
                if (entryName == name || entryName.StartsWith(backupPrefix, StringComparison.Ordinal))
                {
                    paths.AddRange(StringsIn(File.ReadAllBytes(entry)));
                }
            }
            return paths;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every JSON string in <paramref name="bytes"/>, unescaped, including one cut
    /// off by the end of the file. Nothing else about the JSON need be valid.
    /// </summary>
    internal static List<string> StringsIn(byte[] bytes)
    {
        var found = new List<string>();
        var i = 0;
        while (i < bytes.Length)
        {
            if (bytes[i] != (byte)'"')
            {
                i++;
                continue;
            }
            var start = i++;
            while (i < bytes.Length && bytes[i] != (byte)'"')
            {
                i += bytes[i] == (byte)'\\' ? 2 : 1;
            }
            var end = Math.Min(i + 1, bytes.Length);
            found.Add(Unescape(bytes.AsSpan(start, end - start)));
            i = end;
        }
        return found;
    }

    /// <summary>A string literal's text, or, if it does not parse, its raw bytes after the opening quote.</summary>
    static string Unescape(ReadOnlySpan<byte> literal)
    {
        try
        {
            var reader = new Utf8JsonReader(literal);
            if (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Cut off, or a bad escape: fall through to the raw text.
        }
        catch (InvalidOperationException)
        {
            // Text that is not valid UTF-8 in a JSON string.
        }
        return Encoding.UTF8.GetString(literal[1..]);
    }
}
