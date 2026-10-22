using Tack.App.Pinboard.Contract;
using Tack.Core.Settings;

namespace Tack.App;

/// <summary>
/// Changing a setting, in one place, so the tray (and later Settings) never
/// disagree: each change is saved, applied to whatever acts on it, and, if it
/// changes how the board looks or sounds, told to the board as
/// <see cref="BoardFeed.LookChanged"/>.
/// </summary>
sealed class SettingsChanges(AppState state, Reveal reveal, BoardFeed feed)
{
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

    /// <summary>Turns the pin and pop sounds on or off.</summary>
    public void SetSound(bool on) => Change(s => s with { Sound = on });

    /// <summary>Turns revealing at the top edge on or off; the edge poller follows at once.</summary>
    public void SetEdgeReveal(bool on) => Change(s => s with { EdgeReveal = on });

    /// <summary>Applies <paramref name="change"/> and saves the settings if it changed any.</summary>
    public void Change(Func<TackSettings, TackSettings> change)
    {
        TackSettings before, after;
        lock (state.Gate)
        {
            before = state.Settings;
            after = change(before);
            if (after == before)
            {
                return;
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
    }
}
