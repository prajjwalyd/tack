using System.Diagnostics;
using Tack.Core.Diagnostics;
using Tack.Windows.Clipboard;
using Tack.Windows.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Tack.Windows.Selection;

/// <summary>
/// Pinning the selection (Win+Alt+C): Tack asks the app in front to copy what
/// is selected, reads the copy, and puts the user's clipboard back as it was.
/// Before sending Ctrl+C it waits for the shortcut's own keys to be let go, so
/// the app does not read Win+Alt+Ctrl+C. The keys go only to the
/// <see cref="Target"/>, only its app's copy is read, and the user's clipboard
/// is put back only if nobody else wrote to it meanwhile.
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
    /// Waits for the shortcut's keys to be let go, then copies the selection in
    /// <paramref name="target"/> (the window in front when the shortcut fired),
    /// reads it and restores the clipboard. Nothing is sent if another window
    /// has come to the front meanwhile. Takes up to about two seconds, on the
    /// thread pool; safe to start from the UI thread. <paramref name="owner"/> is
    /// the host window, named as the one holding the clipboard open.
    /// </summary>
    public static Task<Grab> GrabAsync(Target? target, nint owner) => Task.Run(() => RunAsync(target, owner));

    static async Task<Grab> RunAsync(Target? target, nint owner)
    {
        var started = Stopwatch.GetTimestamp();
        var wait = new ReleaseWait(TimeSpan.Zero);
        while (true)
        {
            var step = wait.Step(s_modifiers.Any(Pointer.KeyDown), Stopwatch.GetElapsedTime(started));
            if (step == Release.Ready)
            {
                break;
            }
            if (step == Release.TimedOut)
            {
                return Grab.Nothing with { KeysHeld = true };
            }
            await Task.Delay(s_poll).ConfigureAwait(false);
        }
        if (target == null)
        {
            Trace("no window was in front");
            return Grab.Nothing;
        }
        s_tally.Begin();
        try
        {
            return await CopyAndReadAsync(owner, target).ConfigureAwait(false);
        }
        finally
        {
            s_tally.EndAfterTail();
        }
    }

    static async Task<Grab> CopyAndReadAsync(nint owner, Target target)
    {
        var before = ClipboardSnapshot.Take(owner);
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
            await Task.Delay(s_poll).ConfigureAwait(false);
        }
        await Task.Delay(s_secondStep).ConfigureAwait(false);
        var what = await SystemClipboard.ReadAsync(owner).ConfigureAwait(false);
        var read = SystemClipboard.Sequence();
        if (read != sequence && !target.WroteClipboard())
        {
            // What was read may be another app's; the clipboard is theirs now.
            Trace("another app wrote to the clipboard while the copy was read");
            return new Grab(ClipboardContent.Empty, ClipboardFate.LeftNewer);
        }
        return new Grab(what, Restore(before, owner, read));
    }

    static ClipboardFate Restore(ClipboardSnapshot? before, nint owner, uint since)
    {
        if (before == null)
        {
            return new ClipboardFate.Failed("the clipboard could not be read before the copy");
        }
        try
        {
            return before.Restore(owner, since) == Restored.Done ? ClipboardFate.Restored : ClipboardFate.LeftNewer;
        }
        catch (IOException e)
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
