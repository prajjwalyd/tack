using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Tack.App.Settings;

/// <summary>
/// What the Settings window hands each of its sections: the one service every
/// change goes through, the way to another page or group, the window's spoken
/// status line, and word of the window losing the focus or closing. Every
/// member is for the UI thread.
/// </summary>
sealed class SettingsContext
{
    readonly Action<string> _go;
    readonly Func<bool> _onPhonePage;
    readonly Action _focusTop;
    readonly Action<string> _announce;

    public SettingsContext(
        SettingsChanges settings, FrameworkElement root, DispatcherQueue ui,
        Action<string> go, Func<bool> onPhonePage, Action focusTop, Action<string> announce)
    {
        (Settings, Root, Ui) = (settings, root, ui);
        (_go, _onPhonePage, _focusTop, _announce) = (go, onPhonePage, focusTop, announce);
    }

    /// <summary>Reads and changes the settings, Start with Windows and the shortcuts; saves, and tells the board and the tray.</summary>
    public SettingsChanges Settings { get; }

    /// <summary>The window's content: for a part that must hear every key and click first (the shortcut field).</summary>
    public FrameworkElement Root { get; }

    /// <summary>The UI thread, for timers and for work that comes back from elsewhere.</summary>
    public DispatcherQueue Ui { get; }

    /// <summary>This version of Tack, as "0.3.0".</summary>
    public string Version => BuildInfo.Version;

    /// <summary>Whether the phone page is showing rather than the list.</summary>
    public bool OnPhonePage => _onPhonePage();

    /// <summary>The window lost the keyboard focus or is closing: anything listening stops, anything paused is given back.</summary>
    public event Action? Leaving;

    /// <summary>Goes where an id names, as a link would: "phone" is the phone page, any other id a group of the list.</summary>
    public void Go(string id) => _go(id);

    /// <summary>Moves the keyboard focus to the top of the page showing (its first control), as when the focused control goes away.</summary>
    public void FocusTop() => _focusTop();

    /// <summary>Says <paramref name="text"/> to a screen reader, politely, without moving anything on screen.</summary>
    public void Announce(string text) => _announce(text);

    internal void RaiseLeaving() => Leaving?.Invoke();
}
