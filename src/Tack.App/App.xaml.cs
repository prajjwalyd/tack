using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Tack.Core;
using Tack.Core.Captures;
using Tack.Core.Settings;
using Tack.Core.Storage;
using Tack.Windows.Host;
using Tack.Windows.Screenshots;
using Tack.Windows.Startup;

namespace Tack.App;

/// <summary>
/// Tack running in the tray. It opens no window until there is something to
/// show, and keeps running with none open. Startup wires the few services
/// together in the order Tack 0.2 started them.
/// </summary>
public partial class App : Application
{
    readonly AppIdentity _identity;
    readonly TackPaths _paths;
    Storage? _storage;
    HostWindow? _host;
    Shortcuts? _shortcuts;
    Tray? _tray;
    ScreenshotFiles? _screenshots;
    CaptureFlow? _captures;

    public App(AppIdentity identity, TackPaths paths)
    {
        (_identity, _paths) = (identity, paths);
        InitializeComponent();
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _storage = new Storage(_paths);
        var state = AppState.Load(_storage);
        _host = new HostWindow();
        var reveal = new Reveal(DispatcherQueue.GetForCurrentThread());
        var saves = new PendingSaves();
        var prints = new Prints(state, _storage, saves);
        var saving = new CaptureSaving(state, prints, saves, _paths.Captures);
        _captures = new CaptureFlow(prints, saving, saves, reveal);
        var pinning = new Pinning(state, prints, _captures, reveal, _paths.Notes, _host.Handle);

        TackSettings settings;
        lock (state.Gate)
        {
            settings = state.Settings;
        }
        // Registered before the tray is built, as 0.2 did, so its menu can one
        // day say if another app has one of them.
        _shortcuts = new Shortcuts(_host, settings, reveal, pinning);
        var autostart = Autostart.For(_identity);
        _tray = new Tray(_host, _identity, autostart, reveal, Quit);
        // Keeps the Run value pointing at this exe if it was moved, unless this
        // is a build tried out against other folders.
        if (!_paths.FromHome)
        {
            autostart.Refresh();
        }
        new Restore(state, prints, _paths.Notes).Start();
        _screenshots = new ScreenshotFiles(state, prints, reveal);
        _screenshots.Watch(ScreenshotsFolder.Find());
        _captures.Start(_host);
    }

    /// <summary>
    /// Stops taking in new prints, writes whatever save is still waiting (board.json
    /// is written a moment after each change), and ends Tack.
    /// </summary>
    void Quit()
    {
        _captures?.Dispose();
        _screenshots?.Dispose();
        _shortcuts?.Dispose();
        _storage?.Flush();
        _tray?.Dispose();
        _host?.Dispose();
        Exit();
    }
}
