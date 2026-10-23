using System.Threading.Channels;
using Tack.Core.Diagnostics;
using Tack.Windows.Clipboard;

namespace Tack.Windows.Snips;

/// <summary>
/// Reads heard captures off the clipboard one at a time, in the order they
/// came, so they are saved and pinned in that order. It waits on a channel
/// while there is nothing to read, and with timers between tries: no thread is
/// held and nothing polls.
/// </summary>
sealed class SnipReader
{
    /// <summary>How long a capture is left alone before it is read.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(100);

    /// <summary>Reading the picture is tried for about a second.</summary>
    public const int ReadTries = 6;
    public static readonly TimeSpan ReadGap = TimeSpan.FromMilliseconds(200);

    readonly Channel<HeardSnip> _heard = Channel.CreateUnbounded<HeardSnip>(new UnboundedChannelOptions { SingleReader = true });
    readonly Action<Snip> _deliver;

    /// <summary>The copy read last; only the reader uses it.</summary>
    uint? _last;

    /// <param name="deliver">Gets each capture, on a thread-pool thread.</param>
    public SnipReader(Action<Snip> deliver)
    {
        _deliver = deliver;
        _ = Task.Run(ReadAllAsync);
    }

    public void Add(HeardSnip heard) => _heard.Writer.TryWrite(heard);

    /// <summary>Stops once what was heard so far is read.</summary>
    public void Complete() => _heard.Writer.TryComplete();

    async Task ReadAllAsync()
    {
        await foreach (var heard in _heard.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            string outcome;
            try
            {
                outcome = await ReadAsync(heard).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                outcome = $"failed: {e}";
            }
            DevTrace.Write($"clipboard #{heard.Sequence} -> {outcome}");
        }
    }

    /// <summary>
    /// Reads the capture heard at its sequence number: not if it was marked
    /// private, and only if nothing else reached the clipboard meanwhile.
    /// </summary>
    async Task<string> ReadAsync(HeardSnip heard)
    {
        // A busy UI thread hears several changes at once, each with the
        // number the clipboard has by then: that copy is read once.
        if (heard.Sequence == _last)
        {
            return "ignored (heard twice)";
        }
        _last = heard.Sequence;
        // Snipping Tool sets the clipboard, then flushes it: by then Windows
        // has the whole picture and need not ask Snipping Tool to draw it.
        await Task.Delay(Settle).ConfigureAwait(false);
        var (picture, why) = await PictureTaker.TakeAsync(
            () => ClipboardView.TakePictureAsync(heard.Sequence), ReadTries, ReadGap).ConfigureAwait(false);
        if (picture == null)
        {
            return why switch
            {
                Untaken.Busy => "ignored (the clipboard stayed busy)",
                Untaken.Replaced => "ignored (replaced before it was read)",
                Untaken.Private => "ignored (marked private)",
                _ => "ignored (unreadable)",
            };
        }
        if (await picture.DecodeAsync().ConfigureAwait(false) is not { } decoded)
        {
            return "ignored (undecodable)";
        }
        _deliver(new Snip(decoded, heard.Pointer));
        return "read";
    }
}
