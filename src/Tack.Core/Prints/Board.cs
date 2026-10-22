using System.Diagnostics;
using System.Globalization;
using Tack.Core.Files;
using Tack.Core.Notes;
using Tack.Core.Thumbnails;

namespace Tack.Core.Prints;

/// <summary>
/// The board model: which prints are pinned, in what order, which are kept,
/// and which have aged out. The prints always hang in row order (see
/// <see cref="History"/>). Every change returns what happened instead of acting
/// on it, so the app decides what to save, show and clean up.
/// </summary>
public sealed class Board
{
    /// <summary>
    /// With auto-save on, Snipping Tool both saves the file and copies the image;
    /// arrivals of the same pixels this close together are one screenshot.
    /// </summary>
    public static readonly TimeSpan TwinWindow = TimeSpan.FromSeconds(5);

    List<Print> _prints = [];
    long _nextId = 1;

    /// <summary>In row order: kept prints first, then the newest.</summary>
    public IReadOnlyList<Print> Prints => _prints;

    public IReadOnlyList<string> Order() => [.. _prints.Select(p => p.Id)];

    public Print? Find(string id) => _prints.Find(p => p.Id == id);

    public Print? FindPath(string path) => _prints.Find(Showing(path));

    /// <summary>A note already on the board with exactly this text.</summary>
    public Print? FindNote(string text) => _prints.Find(p => p.Note?.Text == text);

    /// <summary>Pins a screenshot at <paramref name="nowMs"/>, wall-clock ms since the Unix epoch.</summary>
    public PinOutcome Pin(string path, Thumb thumb, Origin origin, Arrival arrival, long nowMs)
    {
        if (FindPath(path) != null)
        {
            return PinOutcome.Exists.Instance;
        }
        if (arrival is Arrival.Live live && TwinOf(origin, thumb, live.At) is >= 0 and var index)
        {
            if (origin == Origin.Capture)
            {
                return PinOutcome.Duplicate.Instance;
            }
            // Keep the user's own file rather than Tack's copy of it.
            var twin = _prints[index];
            var merged = twin with
            {
                Path = path,
                Origin = Origin.Folder,
                Name = PathRules.FileName(path),
                Thumb = thumb,
                Stamp = FileStamp.Of(path),
            };
            _prints[index] = merged;
            return new PinOutcome.Merged(merged, twin.Path);
        }
        return Insert(NewPrint(path, arrival, nowMs) with { Thumb = thumb, Origin = origin }, arrival, nowMs);
    }

    /// <summary>
    /// Pins a note whose text is saved at <paramref name="path"/>, one of Tack's
    /// own files. A live note with the same text as one on the board is not
    /// pinned again.
    /// </summary>
    public PinOutcome PinNote(string path, NoteBody body, Arrival arrival, long nowMs)
    {
        if (FindPath(path) != null || (arrival is Arrival.Live && FindNote(body.Text) != null))
        {
            return PinOutcome.Exists.Instance;
        }
        var note = NewPrint(path, arrival, nowMs) with { Kind = Kind.Note, Origin = Origin.Capture, Note = body };
        return Insert(note, arrival, nowMs);
    }

    /// <summary>Takes a print off the board and hands it back.</summary>
    public Print? Remove(string id)
    {
        var index = _prints.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return null;
        }
        var print = _prints[index];
        _prints.RemoveAt(index);
        return print;
    }

    /// <summary>
    /// Keeps a print (it moves to the end of the kept ones) or stops keeping it
    /// (it goes back by when it was pinned, and ages out at once if it is past
    /// the history's limits). Keeping a kept print changes nothing.
    /// </summary>
    public KeepChange? SetKept(string id, bool kept, long nowMs)
    {
        var index = _prints.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return null;
        }
        var before = Order();
        var print = _prints[index];
        if (print.Kept != kept)
        {
            print = print with { KeptAt = kept ? nowMs : null };
            _prints[index] = print;
        }
        Arrange();
        var moved = !Order().SequenceEqual(before);
        return new KeepChange(print, moved, kept ? [] : AgeOut(nowMs));
    }

    /// <summary>Takes off every unkept print past the history's limits and hands them back, in row order.</summary>
    public IReadOnlyList<Print> AgeOut(long nowMs) => History.TakeAged(_prints, p => (p.KeptAt, p.PinnedAt), nowMs);

    /// <summary>
    /// Points a print at its file's new place. Returns the updated print and the
    /// old path, so a move that fails can be undone.
    /// </summary>
    public (Print Print, string OldPath)? Repoint(string id, string path, Origin origin)
    {
        var index = _prints.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return null;
        }
        var old = _prints[index];
        _prints[index] = old with { Path = path, Origin = origin, Name = PathRules.FileName(path) };
        return (_prints[index], old.Path);
    }

    /// <summary>
    /// Notes the file's current stamp once Tack has finished writing a capture's
    /// file: from then on only an edit counts as a change. False if no print
    /// shows that file any more.
    /// </summary>
    public bool RefreshStamp(string path) => Update(Showing(path), p => p with { Stamp = FileStamp.Of(path) }) != null;

    /// <summary>Swaps in a note's text after its file was edited; null if no note shows that file.</summary>
    public Print? UpdateNote(string path, NoteBody body)
    {
        var showing = Showing(path);
        return Update(p => p.IsNote && showing(p), p => p with { Note = body, Stamp = FileStamp.Of(path) });
    }

    /// <summary>Swaps in a fresh thumbnail after the file was edited.</summary>
    public Print? UpdateThumb(string path, Thumb thumb) =>
        Update(Showing(path), p => p with { Thumb = thumb, Stamp = FileStamp.Of(path) });

    static Predicate<Print> Showing(string path)
    {
        var key = PathRules.Key(path);
        return p => PathRules.Key(p.Path) == key;
    }

    Print? Update(Predicate<Print> match, Func<Print, Print> change)
    {
        var index = _prints.FindIndex(match);
        if (index < 0)
        {
            return null;
        }
        return _prints[index] = change(_prints[index]);
    }

    /// <summary>
    /// The other half of an auto-saved screenshot: a print of the other origin
    /// that arrived moments ago with the same size and pixels, or -1. Only known,
    /// matching hashes make two pictures one. Notes have no twins.
    /// </summary>
    int TwinOf(Origin origin, Thumb thumb, TimeSpan now)
    {
        if (thumb.Hash is not { } hash)
        {
            return -1;
        }
        return _prints.FindIndex(p => p.Kind == Kind.Image
            && p.Origin != origin
            && p.Width == thumb.Width
            && p.Height == thumb.Height
            && p.Hash == hash
            && p.Arrived is { } arrived
            && (now > arrived ? now - arrived : TimeSpan.Zero) < TwinWindow);
    }

    /// <summary>A fresh print for <paramref name="path"/> with a new id and the times <paramref name="arrival"/> gives.</summary>
    Print NewPrint(string path, Arrival arrival, long nowMs)
    {
        var (pinnedAt, keptAt, arrived) = arrival switch
        {
            Arrival.Live live => (nowMs, (long?)null, (TimeSpan?)live.At),
            Arrival.Restored restored => (restored.PinnedAt, restored.KeptAt, null),
            _ => throw new UnreachableException(),
        };
        return new Print
        {
            Id = (_nextId++).ToString(CultureInfo.InvariantCulture),
            Path = path,
            Name = PathRules.FileName(path),
            PinnedAt = pinnedAt,
            KeptAt = keptAt,
            Arrived = arrived,
            Stamp = FileStamp.Of(path),
        };
    }

    /// <summary>Puts a new print in its place and ages out what that pushes past the history's limits.</summary>
    PinOutcome Insert(Print print, Arrival arrival, long nowMs)
    {
        // A live print goes ahead of any pinned at the same moment; a restored
        // one comes back in the order it was saved.
        if (arrival is Arrival.Live)
        {
            _prints.Insert(0, print);
        }
        else
        {
            _prints.Add(print);
        }
        Arrange();
        var aged = History.TakeAged(_prints, p => (p.KeptAt, p.PinnedAt), nowMs);
        var index = aged.FindIndex(p => p.Id == print.Id);
        if (index < 0)
        {
            return new PinOutcome.New(print, aged);
        }
        var expired = aged[index];
        aged.RemoveAt(index);
        return new PinOutcome.Expired(expired, aged);
    }

    /// <summary>Puts the prints in row order. Stable (LINQ's OrderBy), so ties keep their places.</summary>
    void Arrange() => _prints = [.. _prints.OrderBy(p => (p.KeptAt, p.PinnedAt), History.RowOrder)];
}
