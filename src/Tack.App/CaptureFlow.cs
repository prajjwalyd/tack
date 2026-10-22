using System.Threading.Channels;
using Tack.App.Pinboard.Contract;
using Tack.Core.Captures;
using Tack.Core.Diagnostics;
using Tack.Core.Prints;
using Tack.Core.Reveal;
using Tack.Windows.Capture;
using Tack.Windows.Host;
using Tack.Windows.Imaging;
using Tack.Windows.Snips;

namespace Tack.App;

/// <summary>
/// Captures: Snipping Tool pictures on the clipboard. Each one is saved in
/// Tack's Captures folder and pinned, unless it repeats the previous copy or
/// is the twin of a screenshot file pinned moments ago. Where on screen it was
/// taken is looked up while its pictures are drawn, so it can fly onto the
/// board from there, and its file is written in the background
/// (<see cref="CaptureSaving"/>) while it is pinned.
/// </summary>
sealed class CaptureFlow(AppState state, Prints prints, OwnedFiles files, CaptureSaving saving, PendingSaves saves, Reveal reveal) : IDisposable
{
    /// <summary>Captures in the order they came, pinned one at a time by one reader.</summary>
    readonly Channel<Snip> _snips = Channel.CreateUnbounded<Snip>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Only the reader of <see cref="_snips"/> uses it.</summary>
    readonly RepeatFilter _repeats = new();

    SnipListener? _listener;

    /// <summary>Starts listening to the clipboard; on the UI thread, which owns <paramref name="host"/>.</summary>
    public void Start(HostWindow host)
    {
        _listener = new SnipListener(host);
        _listener.Captured += snip => _snips.Writer.TryWrite(snip);
        Background.Run("pinning captures", PinAllAsync);
    }

    /// <summary>
    /// Pins a picture copied from the selection like a capture: saved in the
    /// Captures folder and pressed onto the board as it comes down. Returns the
    /// trace's decision.
    /// </summary>
    public Task<string> PinPictureAsync(Picture picture) => PinCaptureAsync(new RepeatFilter(), picture, null);

    async Task PinAllAsync()
    {
        await foreach (var snip in _snips.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            string decision;
            try
            {
                decision = await PinCaptureAsync(_repeats, snip.Picture, snip.Pointer).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                decision = $"failed: {e}";
            }
            DevTrace.Write($"capture: {decision}");
        }
    }

    /// <summary>Pins the capture, flying it in from the snip around <paramref name="pointer"/> if there is one.</summary>
    async Task<string> PinCaptureAsync(RepeatFilter repeats, Picture picture, (int X, int Y)? pointer)
    {
        // Taken once: it spots a repeat now and a twin when the print is pinned.
        var hash = picture.Hash();
        if (repeats.IsRepeat(hash, Prints.Now))
        {
            return "duplicate (repeat)";
        }
        DevTrace.Write($"capture: {picture.Width}x{picture.Height}, saving in the background");
        // Where it was taken, looked up while the pictures are drawn.
        var lookup = pointer is { } at ? Task.Run(() => ScreenGrab.Find(picture, at)) : null;
        string path;
        try
        {
            path = saving.Save(picture);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"ignored (cannot save: {e.Message})";
        }
        FlightPicture pictures;
        try
        {
            pictures = await Thumbs.ForFlightAsync(picture, hash).ConfigureAwait(false);
        }
        catch (InvalidDataException e)
        {
            files.Discard([path]);
            return $"ignored (cannot draw: {e.Message})";
        }
        return await PinSavedAsync(path, pictures, await FoundAsync(lookup).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>The lookup's answer; null when there was none to make, or it failed.</summary>
    static async Task<Lookup?> FoundAsync(Task<Lookup>? lookup)
    {
        if (lookup is null)
        {
            return null;
        }
        try
        {
            var found = await lookup.ConfigureAwait(false);
            DevTrace.Write(
                $"capture: flies from {found.Rect} ({(found.Found is { } f ? $"{f.Source}, score {f.Score:0.000}" : "not found, from the pointer")}), "
                + $"{found.Tries} look(s), {found.Cpu.TotalMilliseconds:0.0} ms CPU, {found.Took.TotalMilliseconds:0} ms");
            return found;
        }
        catch (Exception e)
        {
            DevTrace.Write($"capture: the lookup failed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Pins a capture (saved, or still being saved) and, if it is new, brings
    /// the board down: on the monitor where it was taken, flying in from there,
    /// when it came with a place, unless something is full screen; otherwise as
    /// for any new screenshot.
    /// </summary>
    async Task<string> PinSavedAsync(string path, FlightPicture pictures, Lookup? lookup)
    {
        var (place, flight, quietly) = (default(Placement), default(Flight), false);
        if (lookup is { } found)
        {
            if (await reveal.MayRevealNewAsync().ConfigureAwait(false))
            {
                place = Placement.At(found.Rect.Center.X, found.Rect.Center.Y);
                flight = FlightFrom(found, place, pictures.Jpeg);
            }
            else
            {
                quietly = true;
            }
        }
        switch (prints.Pin(path, pictures.Thumb, Origin.Capture, Prints.Live(), flight))
        {
            case PinOutcome.New added:
                if (place is not null)
                {
                    reveal.FlyIn(place);
                }
                else if (!quietly)
                {
                    await reveal.ForNewAsync().ConfigureAwait(false);
                }
                prints.AnnounceLater(added.Aged);
                return "new";
            case PinOutcome.Duplicate:
                // The auto-saved file got there first and is the one to keep. The
                // copy Tack writes was never pinned or shown, and the board found
                // the very same pixels in the user's own file, so it alone is
                // removed outright rather than recycled.
                saves.WhenSaved(path, RemoveDuplicate);
                return "duplicate";
            case PinOutcome.Merged:
                // A merge pins the incoming file in its twin's place, so it stays.
                return "merged";
            default:
                files.Discard([path]);
                return "duplicate";
        }
    }

    /// <summary>
    /// The capture's flight from where it was taken, relative to the board's
    /// window at <paramref name="place"/>, with the speed as it is now and the
    /// one-time tip if it has not been shown yet.
    /// </summary>
    Flight FlightFrom(Lookup lookup, Placement place, byte[] jpeg)
    {
        lock (state.Gate)
        {
            return new Flight(
                lookup.Rect.ToLogical(place.Position, place.Scale),
                jpeg,
                Found: lookup.Found is not null,
                Tip: !state.Settings.SnipTipShown,
                MotionScale: state.Settings.Reveal.MotionScale());
        }
    }

    void RemoveDuplicate(string path)
    {
        if (!saving.InCaptures(path))
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"cannot remove the duplicate capture {Path.GetFileName(path)}: {e.Message}");
        }
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _snips.Writer.TryComplete();
    }
}
