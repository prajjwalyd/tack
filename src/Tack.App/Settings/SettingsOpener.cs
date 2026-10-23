using Tack.Core.Geometry;
using Tack.Core.Phone;
using Tack.Core.Settings;
using Tack.Core.Updates;
using Tack.Windows.Windowing;
using Windows.Graphics;

namespace Tack.App.Settings;

/// <summary>
/// Opens the one Settings window, or brings it forward if it is open, placed
/// under the board on the monitor the board is down on (or will come down on).
/// Created while open: closing it lets the window and its pages go. UI thread
/// only.
/// </summary>
/// <param name="phone">The phone board, which the phone card and page show.</param>
/// <param name="updater">Updates, which the updates section shows.</param>
sealed class SettingsOpener(AppState state, SettingsChanges settings, Reveal reveal, IPhoneLink phone, IUpdater updater)
{
    SettingsWindow? _window;

    /// <summary>The window, while it is open.</summary>
    public SettingsWindow? Window => _window;

    /// <summary>
    /// Opens the window with the keyboard focus, at <paramref name="section"/>
    /// if there is one (<see cref="SettingsRoute"/>; null for the top of the
    /// list). An open window is moved under the board again and told the section.
    /// </summary>
    public void Open(string? section = null)
    {
        var window = _window ?? Create();
        Place(window);
        window.Activate();
        if (section is not null)
        {
            window.Go(section);
        }
    }

    /// <summary>
    /// Shows the window at <paramref name="section"/> without taking the
    /// keyboard, so a key the person was about to press elsewhere cannot answer
    /// what it shows; it does not hold the board, having no focus. A window that
    /// is open stays where it is and flashes its taskbar button instead, as Tack
    /// 0.2 asked for attention. For a device asking to use the board.
    /// </summary>
    public void ShowQuietly(string section)
    {
        if (_window is not null)
        {
            Attention.Ask(_window.Handle);
            return;
        }
        var window = Create();
        Place(window);
        window.AppWindow.Show(activateWindow: false);
        window.Go(section);
    }

    /// <summary>Closes the window, if it is open.</summary>
    public void Close() => _window?.Close();

    SettingsWindow Create()
    {
        var window = new SettingsWindow(settings, reveal, phone, updater);
        window.Closed += (_, _) => _window = null;
        _window = window;
        return window;
    }

    /// <summary>
    /// Moves and sizes the window per <see cref="SettingsPlacement"/> for the
    /// monitor the board is on. The frame is the same size in logical px; on
    /// another monitor it is drawn at that monitor's scale.
    /// </summary>
    void Place(SettingsWindow window)
    {
        var on = BoardMonitor();
        var app = window.AppWindow;
        var own = WindowScale.Of(window.Handle);
        int Frame(int outer, int inner) =>
            (int)Math.Round(Math.Max(outer - inner, 0) / own * on.Scale, MidpointRounding.AwayFromZero);
        var chrome = (Frame(app.Size.Width, app.ClientSize.Width), Frame(app.Size.Height, app.ClientSize.Height));
        var at = SettingsPlacement.For(new Rect(on.Position.X, on.Position.Y, on.Size.W, on.Size.H), on.Scale, chrome);
        // Move, size, move: crossing to a monitor with another DPI makes the
        // window resize itself on the way, which the size then corrects.
        var position = new PointInt32(at.X, at.Y);
        app.Move(position);
        app.Resize(new SizeInt32(at.W, at.H));
        app.Move(position);
    }

    /// <summary>The monitor the board is down on, or will come down on: under the pointer, as a reveal picks it.</summary>
    Placement BoardMonitor()
    {
        lock (state.Gate)
        {
            if (state.View.Shown)
            {
                return Placement.At(state.View.WindowPos.X, state.View.WindowPos.Y);
            }
        }
        return Placement.UnderPointer();
    }
}
