using System.Diagnostics;
using System.Runtime.InteropServices;
using Tack.Core.Diagnostics;
using Tack.Windows.Clipboard;
using Tack.Windows.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Tack.Windows.Selection;

/// <summary>
/// Pinning the selection (Win+Alt+C). Selected text is read through UI
/// Automation (<see cref="SelectedText"/>), which leaves the clipboard alone.
/// Anything else (a picture, files, or an app UI Automation cannot read) is
/// copied by the app in front: Tack waits for the shortcut's own keys to be
/// let go, so the app does not read Win+Alt+Ctrl+C, sends Ctrl+C to the
/// <see cref="Target"/> only, reads only its app's copy, and puts the user's
/// clipboard back, kept out of clipboard history, unless another app wrote to
/// it meanwhile. The copy the app makes is its own and goes into history like
/// any other.
/// </summary>
public static class SelectionGrab
{
    /// <summary>How long the app in front gets to copy after Ctrl+C.</summary>
    static readonly TimeSpan s_copyPatience = TimeSpan.FromMilliseconds(400);

    /// <summary>Some apps set the clipboard in two steps; the second gets this long to land.</summary>
    static readonly TimeSpan s_secondStep = TimeSpan.FromMilliseconds(40);

    static readonly TimeSpan s_poll = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// A key code Windows leaves unassigned: pressing it does nothing anywhere,
    /// but it counts as "another key" while Alt or Win is down.
    /// </summary>
    const VIRTUAL_KEY MaskKey = (VIRTUAL_KEY)0xE8;

    static readonly VIRTUAL_KEY[] s_menuKeys = [VIRTUAL_KEY.VK_MENU, VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_RWIN];
    static readonly VIRTUAL_KEY[] s_modifiers =
        [VIRTUAL_KEY.VK_MENU, VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_RWIN, VIRTUAL_KEY.VK_CONTROL, VIRTUAL_KEY.VK_SHIFT];

    /// <summary>Grabs, and 250 ms after each, during which the snipping listener ignores the clipboard.</summary>
    static readonly GrabTally s_tally = new(TimeSpan.FromMilliseconds(250));

    /// <summary>Whether a grab is changing the clipboard right now.</summary>
    internal static bool Grabbing => s_tally.Any;

    /// <summary>
    /// Call the moment the shortcut fires, on the UI thread: while Alt or Win is
    /// held, a press of an unassigned key keeps their release from opening the
    /// menu bar of the app in front, or Start.
    /// </summary>
    public static void MaskMenu()
    {
        if (s_menuKeys.Any(Pointer.KeyDown))
        {
            KeyInput.Send([KeyInput.Key(MaskKey, false), KeyInput.Key(MaskKey, true)]);
        }
    }

    /// <summary>
    /// Reads the selection in <paramref name="target"/> (the window in front
    /// when the shortcut fired). Takes up to about two seconds when the app has
    /// to copy, on a thread of its own; safe to start from the UI thread.
    /// </summary>
    public static Task<Grab> GrabAsync(Target? target)
    {
        if (target == null)
        {
            Trace("no window was in front");
            return Task.FromResult(Grab.Nothing);
        }
        // The clipboard thread suits UI Automation too, and the copy's
        // reading and putting back must share one thread.
        return ClipboardThread.RunAsync(() => Run(target));
    }

    static Grab Run(Target target)
    {
        if (SelectedText.Read(target) is { } selected)
        {
            return new Grab(selected, ClipboardFate.Untouched);
        }
        if (!KeysLetGo())
        {
            return Grab.Nothing with { KeysHeld = true };
        }
        s_tally.Begin();
        try
        {
            return CopyAndRead(target);
        }
        finally
        {
            s_tally.EndAfterTail();
        }
    }

    /// <summary>Waits for the shortcut's keys to be let go; false if they are held too long.</summary>
    static bool KeysLetGo()
    {
        var started = Stopwatch.GetTimestamp();
        var wait = new ReleaseWait(TimeSpan.Zero);
        while (true)
        {
            switch (wait.Step(s_modifiers.Any(Pointer.KeyDown), Stopwatch.GetElapsedTime(started)))
            {
                case Release.Ready:
                    return true;
                case Release.TimedOut:
                    return false;
            }
            Thread.Sleep(s_poll);
        }
    }

    static Grab CopyAndRead(Target target)
    {
        SavedClipboard? before;
        try
        {
            before = SavedClipboard.Take();
        }
        catch (COMException)
        {
            before = null;
        }
        var sequence = SystemClipboard.Sequence();
        if (!target.StillInFront())
        {
            Trace("another window came to the front; no keys sent");
            return Grab.Nothing;
        }
        if (!KeyInput.Send(CopyKeys(target.Terminal)))
        {
            Trace("SendInput refused the keys");
            return Grab.Nothing;
        }
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            var now = SystemClipboard.Sequence();
            if (now != sequence)
            {
                sequence = now;
                if (target.WroteClipboard())
                {
                    break;
                }
                Trace("another app wrote to the clipboard; still waiting for the copy");
            }
            if (Stopwatch.GetElapsedTime(started) >= s_copyPatience)
            {
                Trace("the app in front did not copy anything");
                return Grab.Nothing;
            }
            Thread.Sleep(s_poll);
        }
        Thread.Sleep(s_secondStep);
        ClipboardContent what;
        try
        {
            what = ClipboardView.Read();
        }
        catch (Exception e)
        {
            // The user's clipboard still goes back.
            Trace($"the copy could not be read ({e.Message})");
            what = ClipboardContent.Empty;
        }
        var read = SystemClipboard.Sequence();
        if (read != sequence && !target.WroteClipboard())
        {
            // What was read may be another app's; the clipboard is theirs now.
            Trace("another app wrote to the clipboard while the copy was read");
            return new Grab(ClipboardContent.Empty, ClipboardFate.LeftNewer);
        }
        return new Grab(what, PutBack(before, read));
    }

    static ClipboardFate PutBack(SavedClipboard? before, uint since)
    {
        if (before == null)
        {
            return new ClipboardFate.Failed("the clipboard could not be read before the copy");
        }
        try
        {
            if (!before.PutBack(since))
            {
                return ClipboardFate.LeftNewer;
            }
            return before.Unread > 0
                ? new ClipboardFate.Failed($"{before.Unread} format(s) could not be read before the copy")
                : ClipboardFate.Restored;
        }
        catch (Exception e) when (e is COMException or IOException)
        {
            return new ClipboardFate.Failed(e.Message);
        }
    }

    /// <summary>Ctrl+C, or Ctrl+Insert for a terminal: Insert is an extended key, told apart from the keypad's 0 by its flag.</summary>
    static INPUT[] CopyKeys(bool terminal) => terminal
        ?
        [
            KeyInput.Key(VIRTUAL_KEY.VK_CONTROL, false),
            KeyInput.Extended(VIRTUAL_KEY.VK_INSERT, false),
            KeyInput.Extended(VIRTUAL_KEY.VK_INSERT, true),
            KeyInput.Key(VIRTUAL_KEY.VK_CONTROL, true),
        ]
        :
        [
            KeyInput.Key(VIRTUAL_KEY.VK_CONTROL, false),
            KeyInput.Key(VIRTUAL_KEY.VK_C, false),
            KeyInput.Key(VIRTUAL_KEY.VK_C, true),
            KeyInput.Key(VIRTUAL_KEY.VK_CONTROL, true),
        ];

    /// <summary>Development builds say why a grab found nothing.</summary>
    static void Trace(string why) => DevTrace.Write($"pin selection: {why}");
}
