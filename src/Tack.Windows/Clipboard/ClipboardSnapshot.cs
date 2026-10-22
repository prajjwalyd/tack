using Tack.Core.Diagnostics;

namespace Tack.Windows.Clipboard;

/// <summary>
/// What was on the clipboard, to put back after pinning a selection. Holds
/// every format that is plain memory. Not kept: bitmaps and metafiles as GDI
/// objects (Windows makes bitmaps again from the DIB that is kept), and
/// embedded or linked OLE objects (an Office object copied as such comes back
/// as its text, RTF, HTML and picture, without the live object).
/// </summary>
sealed class ClipboardSnapshot
{
    /// <summary>The most a snapshot holds; past this the clipboard is not restored.</summary>
    const long Limit = 256L * 1024 * 1024;

    readonly List<(uint Format, byte[] Bytes)> _formats;

    /// <summary>
    /// Formats that should have been kept but whose data could not be read (an
    /// owner that failed to draw it on demand): the restore is partial.
    /// </summary>
    readonly int _unread;

    /// <summary>Which privacy markers were present, data or not.</summary>
    readonly bool[] _marks;

    ClipboardSnapshot(List<(uint, byte[])> formats, int unread, bool[] marks) =>
        (_formats, _unread, _marks) = (formats, unread, marks);

    /// <summary>
    /// Takes a snapshot of the clipboard, or null if it could not be opened or
    /// holds more than can sensibly be kept. Blocks briefly; not on the UI thread.
    /// </summary>
    public static ClipboardSnapshot? Take(nint owner)
    {
        using var open = ClipboardSession.Open(owner);
        if (open == null)
        {
            return null;
        }
        var formats = new List<(uint, byte[])>();
        var (unread, total) = (0, 0L);
        foreach (var format in open.Formats())
        {
            if (!ClipboardFormats.Restorable(format))
            {
                continue;
            }
            if (open.Bytes(format) is not { } bytes)
            {
                // A marker's presence is kept apart from its data.
                if (!PrivacyMarkers.IsMarker(format))
                {
                    unread++;
                }
                continue;
            }
            total += bytes.Length;
            if (total > Limit)
            {
                DevTrace.Write($"clipboard: it holds over {Limit >> 20} MB; it will not be restored");
                return null;
            }
            formats.Add((format, bytes));
        }
        return new ClipboardSnapshot(formats, unread, PrivacyMarkers.Read(open));
    }

    /// <summary>
    /// Puts the snapshot back, unless the clipboard changed after
    /// <paramref name="since"/> (a sequence number). Throws
    /// <see cref="IOException"/> when the clipboard stays busy or only part of
    /// the snapshot went back. Blocks briefly; not on the UI thread.
    /// </summary>
    public Restored Restore(nint owner, uint since)
    {
        using var open = ClipboardSession.Open(owner) ?? throw new IOException("The clipboard is busy.");
        // Checked while it is open, so nobody can write in between.
        if (SystemClipboard.Sequence() != since)
        {
            return Restored.Superseded;
        }
        open.Empty();
        var (failed, total) = (_unread, _unread);
        foreach (var write in RestorePlan.Of(_formats, _marks, PrivacyMarkers.Formats))
        {
            // A marker added only to keep the copy out of history is best effort.
            if (write.Counts)
            {
                total++;
                failed += TrySet(open, write) ? 0 : 1;
            }
            else
            {
                TrySet(open, write);
            }
        }
        if (failed > 0)
        {
            throw new IOException($"{failed} of {total} formats could not be put back.");
        }
        return Restored.Done;
    }

    static bool TrySet(ClipboardSession open, RestoreWrite write)
    {
        if (write.Format == 0)
        {
            return false;
        }
        try
        {
            open.Set(write.Format, write.Bytes);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
