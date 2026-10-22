using System.Text.Json;
using Tack.Core.Diagnostics;
using Tack.Core.Prints;
using Tack.Core.Storage.Json;

namespace Tack.Core.Storage;

/// <summary>
/// What survives a restart: board.json, holding the pinned prints in row order
/// and the settings. Only paths are stored, never images or a note's text. A
/// file from a newer Tack, or one that lists no prints, is read as far as it
/// goes but never trusted for cleanup, and is backed up before Tack writes over it.
/// </summary>
public static class BoardFile
{
    /// <summary>
    /// Reads board.json and says how that went. Anything short of
    /// <see cref="Loaded.Ok"/> still gives a usable board. A file that cannot be
    /// used as it is gets copied to board.json.bad-&lt;nowMs&gt; first, so no save
    /// can lose it. A .tmp left by an interrupted save is removed: it never
    /// replaced the real file.
    /// </summary>
    public static (Saved Saved, Loaded Loaded) Load(string file, long nowMs)
    {
        TryDelete(TempFile(file));
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(file);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return (new Saved(), Loaded.Missing.Instance);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (new Saved(), new Loaded.Unreadable($"cannot read it: {e.Message}", BackUp(file, nowMs)));
        }
        var (saved, loaded) = Parse(bytes, nowMs);
        return loaded is Loaded.Unreadable unreadable
            ? (saved, unreadable with { Backup = BackUp(file, nowMs) })
            : (saved, loaded);
    }

    /// <summary>Writes board.json from a snapshot, replacing the file in one step.</summary>
    public static void Write(string file, Snapshot snapshot) =>
        Replace(file, JsonSerializer.SerializeToUtf8Bytes(StoredBoard.Of(snapshot.Prints, snapshot.Settings), BoardJson.Board));

    /// <summary>Where a save is written before it replaces board.json.</summary>
    public static string TempFile(string file) => file + ".tmp";

    /// <summary>
    /// Parses board.json's contents. Contents that do not parse are an empty
    /// board; a newer schema, or no print list, is read as far as it goes but
    /// reported unreadable. The backup is made by <see cref="Load"/>.
    /// </summary>
    internal static (Saved Saved, Loaded Loaded) Parse(byte[] bytes, long nowMs)
    {
        StoredBoard? stored;
        try
        {
            stored = JsonSerializer.Deserialize(bytes, BoardJson.Board);
        }
        catch (JsonException e)
        {
            stored = null;
            DevTrace.Write($"board.json does not parse: {e.Message}");
        }
        if (stored is null)
        {
            return (new Saved(), new Loaded.Unreadable("it is not a board this build can read", null));
        }
        Loaded loaded = stored switch
        {
            { Version: > StoredBoard.CurrentVersion } => new Loaded.Unreadable(
                $"it was written by a newer Tack (schema {stored.Version}; this build knows up to {StoredBoard.CurrentVersion})", null),
            { Prints: null } => new Loaded.Unreadable("it lists no prints at all", null),
            _ => Loaded.Ok.Instance,
        };
        var prints = (stored.Prints ?? []).Select(p => ToSaved(p, nowMs)).OfType<SavedPrint>()
            .OrderBy(p => (p.KeptAt, p.PinnedAt), History.RowOrder);
        return (new Saved { Settings = stored.ToSettings(), Prints = [.. prints] }, loaded);
    }

    static SavedPrint? ToSaved(StoredPrint stored, long nowMs)
    {
        Kind kind;
        switch (stored.Kind)
        {
            case null: kind = Kind.Image; break;
            case "note": kind = Kind.Note; break;
            default:
                DevTrace.Write($"skipping a pinned \"{stored.Kind}\", a kind of print this build does not know");
                return null;
        }
        var pinnedAt = stored.PinnedAt is { } pinned ? Ms(pinned) : nowMs;
        // A kept print saved without its time counts as kept when pinned.
        long? keptAt = stored.Kept ? stored.KeptAt is { } kept ? Ms(kept) : pinnedAt : null;
        // A note's file is always Tack's own.
        var origin = stored.Capture || kind == Kind.Note ? Origin.Capture : Origin.Folder;
        return new SavedPrint(stored.Path, kind, origin, pinnedAt, keptAt);
    }

    static long Ms(ulong value) => (long)Math.Min(value, long.MaxValue);

    /// <summary>Copies a board.json this build cannot trust to board.json.bad-&lt;nowMs&gt;.</summary>
    static string? BackUp(string file, long nowMs)
    {
        var backup = $"{file}.bad-{nowMs}";
        try
        {
            File.Copy(file, backup, overwrite: true);
            return backup;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"cannot back up board.json: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Replaces <paramref name="file"/> with <paramref name="bytes"/> all at once:
    /// written and flushed to a temporary file beside it, then moved over it. On
    /// NTFS the move swaps the file in one step, so a reader, or the next start
    /// after a crash, finds either the old contents or the new.
    /// </summary>
    static void Replace(string file, byte[] bytes)
    {
        if (Path.GetDirectoryName(file) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }
        var temp = TempFile(file);
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, file, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing to do: the next save writes over it.
        }
    }
}
