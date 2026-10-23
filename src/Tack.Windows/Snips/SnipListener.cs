using System.ComponentModel;
using System.Diagnostics;
using Tack.Core.Diagnostics;
using Tack.Windows.Clipboard;
using Tack.Windows.Foreground;
using Tack.Windows.Host;
using Tack.Windows.Input;
using Tack.Windows.Selection;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Tack.Windows.Snips;

/// <summary>
/// Hears Snipping Tool captures arrive on the clipboard. With "Automatically
/// save original screenshots" off, Win + Shift + S never writes a file, so
/// watching the Screenshots folder alone would miss them. Only Snipping Tool's
/// own copies count: the clipboard is everyone's, and pinning every copied
/// image would be noise. It listens through the host window, so it sleeps
/// until the clipboard changes.
/// </summary>
public sealed class SnipListener : IDisposable
{
    readonly HostWindow _host;
    readonly SnipReader _reader;
    readonly bool _listening;
    bool _disposed;

    /// <summary>
    /// Starts listening; create it on the UI thread, which owns the host window.
    /// If Windows will not let it listen, that is traced and no snip ever arrives.
    /// </summary>
    public SnipListener(HostWindow host)
    {
        _host = host;
        _reader = new SnipReader(host.Handle, snip => Captured?.Invoke(snip));
        host.Listen(PInvoke.WM_CLIPBOARDUPDATE, (_, _) => OnChanged());
        // As in Tack 0.2, Tack runs on without snips rather than not at all;
        // screenshot files and everything else still work.
        _listening = PInvoke.AddClipboardFormatListener((HWND)host.Handle);
        if (!_listening)
        {
            DevTrace.Write($"cannot watch the clipboard: {new Win32Exception().Message}");
        }
    }

    /// <summary>
    /// A capture arrived. Raised on a thread-pool thread, one capture at a
    /// time, in the order they came: save and pin it there, and post to the UI
    /// thread what the board needs.
    /// </summary>
    public event Action<Snip>? Captured;

    /// <summary>
    /// Decides on the spot whether a change is a capture: the owner and the
    /// window in front are only meaningful right now. Reading the picture is
    /// slow and left to the reader.
    /// </summary>
    void OnChanged()
    {
        if (_disposed)
        {
            return;
        }
        // First, so the checks below judge this very change or a later one,
        // which the reader then turns down.
        var sequence = SystemClipboard.Sequence();
        var pointer = Pointer.Position();
        var owner = SystemClipboard.Owner();
        var ignored = SnipJudge.Ignored(
            SelectionGrab.Grabbing,
            owner != 0 && SnippingTool.Owns(owner),
            SystemClipboard.HasPicture());
        Trace(sequence, owner, ignored ?? "reading");
        if (ignored == null)
        {
            _reader.Add(new HeardSnip(sequence, pointer));
        }
    }

    /// <summary>One line per clipboard change in development builds. Window titles are never traced.</summary>
    [Conditional("DEBUG")]
    static void Trace(uint sequence, nint owner, string decision)
    {
        var front = WindowFacts.Front();
        var frontClass = WindowFacts.ClassName(front);
        var ownerExe = owner != 0 ? WindowFacts.ExeName((HWND)owner) : null;
        DevTrace.Write(
            $"clipboard #{sequence} owner={ownerExe ?? "none"} fg={WindowFacts.ExeName(front) ?? "none"} "
            + $"class={(frontClass.Length > 0 ? frontClass : "none")} -> {decision}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_listening)
        {
            PInvoke.RemoveClipboardFormatListener((HWND)_host.Handle);
        }
        _reader.Complete();
    }
}
