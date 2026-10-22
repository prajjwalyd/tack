using Microsoft.UI.Dispatching;
using Tack.App.Pinboard.Contract;
using Tack.Core.Appearance;
using Tack.Core.Reveal;
using Tack.Core.Settings;
using Tack.Core.Shortcuts;
using Tack.Windows.Startup;

namespace Tack.App;

/// <summary>
/// Changing a setting, in one place, so the tray and Settings never disagree:
/// each change is saved, applied to whatever acts on it (the board, the edge
/// poller, the shortcuts, the Run key), and told to whoever shows settings
/// through <see cref="Changed"/>. A change to how the board looks or sounds
/// also reaches the board as <see cref="BoardFeed.LookChanged"/>. A value that
/// is not valid changes nothing. Called on the UI thread, which owns the
/// shortcuts.
/// </summary>
sealed class SettingsChanges(AppState state, Reveal reveal, BoardFeed feed, Shortcuts shortcuts, Autostart autostart, DispatcherQueue ui)
{
    /// <summary>Something Settings shows changed: a setting, Start with Windows or how the shortcuts registered. Raised on the UI thread.</summary>
    public event Action? Changed;

    /// <summary>The settings as they stand now.</summary>
    public TackSettings Current
    {
        get
        {
            lock (state.Gate)
            {
                return state.Settings;
            }
        }
    }

    /// <summary>Whether Tack starts with Windows: read from the Run key each time, not from board.json.</summary>
    public bool Autostart => autostart.Enabled;

    /// <summary>The shortcuts and how registering them went.</summary>
    public ShortcutPair Shortcuts => shortcuts.Current(Current);

    /// <summary>The shortcuts another app has, as "Win+Alt+C".</summary>
    public IEnumerable<string> ShortcutsInUse => shortcuts.InUse(Current);

    /// <summary>Turns the pin and pop sounds on or off.</summary>
    public void SetSound(bool on) => Change(s => s with { Sound = on });

    /// <summary>Turns revealing at the top edge on or off; the edge poller follows at once.</summary>
    public void SetEdgeReveal(bool on) => Change(s => s with { EdgeReveal = on });

    /// <summary>How soon the board appears at the top edge and how quickly it moves.</summary>
    public void SetReveal(RevealSpeed speed) => Change(s => s with { Reveal = speed });

    /// <summary>The board colour.</summary>
    public void SetFelt(Felt felt) => Change(s => s with { Appearance = s.Appearance with { Felt = felt } });

    /// <summary>The frame's finish.</summary>
    public void SetFrame(Frame frame) => Change(s => s with { Appearance = s.Appearance with { Frame = frame } });

    /// <summary>The note paper's colour.</summary>
    public void SetPaper(NotePaper paper) => Change(s => s with { Appearance = s.Appearance with { Paper = paper } });

    /// <summary>The board colour, the frame and the note paper back to the way Tack started; the speed and the rest stay.</summary>
    public void ResetAppearance() => Change(s => s with { Appearance = new AppearanceSettings() });

    /// <summary>Look for a new version once when Tack starts.</summary>
    public void SetUpdatesCheck(bool on) => Change(s => s with { Updates = s.Updates with { Check = on } });

    /// <summary>The phone board's switch, saved; the phone link hears the change and starts or stops serving the board.</summary>
    public void SetPhoneOn(bool on) => Change(s => s with { Phone = s.Phone with { On = on } });

    /// <summary>Removes an allowed device: it has to ask again.</summary>
    public void ForgetDevice(string key) => Change(s => s with { Phone = s.Phone.Forget(key) });

    /// <summary>Adds or removes the Run value.</summary>
    public void SetAutostart(bool on)
    {
        autostart.Set(on);
        Notify();
    }

    /// <summary>
    /// Tries <paramref name="toggle"/> and <paramref name="pin"/> ("Win+Alt+S";
    /// empty turns one off). If both are valid and free they are registered and
    /// saved; otherwise nothing changes and the old ones stay registered.
    /// Says how each requested one fared.
    /// </summary>
    public ShortcutPair SetShortcuts(string toggle, string pin)
    {
        var plan = ShortcutPlan.For(toggle, pin);
        if (!plan.CanTry)
        {
            return plan.Answer;
        }
        var answer = shortcuts.TrySet(plan, Current);
        if (answer.AllWork)
        {
            Apply(s => s with { ToggleShortcut = answer.Toggle.Chord, PinShortcut = answer.Pin.Chord });
        }
        Notify();
        return answer;
    }

    /// <summary>Settings listens for a chord (true) or is done listening; Tack's shortcuts let go meanwhile.</summary>
    public void PauseShortcuts(bool paused) => shortcuts.Pause(paused);

    /// <summary>Applies <paramref name="change"/>, and saves and tells everyone if it changed anything.</summary>
    public void Change(Func<TackSettings, TackSettings> change)
    {
        if (Apply(change))
        {
            Notify();
        }
    }

    /// <summary>Applies <paramref name="change"/> and saves the settings if it changed any; says whether it did.</summary>
    bool Apply(Func<TackSettings, TackSettings> change)
    {
        TackSettings before, after;
        lock (state.Gate)
        {
            before = state.Settings;
            after = change(before);
            if (after == before)
            {
                return false;
            }
            state.Settings = after;
            state.Save();
            if (BoardLook.Of(after) is var look && look != BoardLook.Of(before))
            {
                feed.RaiseLookChanged(look);
            }
        }
        if (after.EdgeReveal != before.EdgeReveal)
        {
            // The poller sleeps while edge reveal is off, and the edge strips follow it.
            reveal.WakePoller();
        }
        return true;
    }

    void Notify()
    {
        if (ui.HasThreadAccess)
        {
            Changed?.Invoke();
        }
        else
        {
            ui.TryEnqueue(() => Changed?.Invoke());
        }
    }
}
