using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Tack.App.Pinboard;
using Tack.App.Pinboard.Contract;
using Tack.Core;
using Tack.Core.Captures;
using Tack.Core.Diagnostics;
using Tack.Core.Settings;
using Tack.Core.Storage;
using Tack.Windows.Edge;
using Tack.Windows.Host;
using Tack.Windows.Screenshots;
using Tack.Windows.Snips;
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
    BoardWindow? _board;
    EdgePoller? _poller;

    public App(AppIdentity identity, TackPaths paths)
    {
        (_identity, _paths) = (identity, paths);
        InitializeComponent();
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => DevTrace.Write($"unhandled on the UI thread: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _storage = new Storage(_paths);
        var state = AppState.Load(_storage);
        _host = new HostWindow();
        var ui = DispatcherQueue.GetForCurrentThread();
        // The board hears every change from here on; it is made hidden now, so the first reveal draws at once.
        var feed = new BoardFeed(ui);
        _board = new BoardWindow();
        var saves = new PendingSaves();
        var files = new OwnedFiles(_storage, saves);
        var prints = new Prints(state, files, feed, ui);
        var unpinning = new Unpinning(state, _storage, saves, files, feed);
        var reveal = new Reveal(state, ui, _board, feed, () =>
        {
            prints.AgeOut();
            _screenshots?.CheckOwned();
        });
        var notices = new Notices(state, reveal, feed);
        var saving = new CaptureSaving(state, unpinning, saves, _paths.Captures);
        _captures = new CaptureFlow(state, prints, files, saving, saves, reveal);
        var pinning = new Pinning(state, prints, _captures, reveal, notices, _paths.Notes, _host.Handle);
        _board.Start(BoardActionsFor(state, saves, files, feed, ui, prints, unpinning, reveal, notices, pinning), feed);

        TackSettings settings;
        lock (state.Gate)
        {
            settings = state.Settings;
        }
        // Registered before the tray is built, as 0.2 did, so its menu can one
        // day say if another app has one of them.
        _shortcuts = new Shortcuts(_host, settings, reveal, pinning);
        var autostart = Autostart.For(_identity);
        _tray = new Tray(_host, _identity, autostart, reveal, unpinning, new SettingsChanges(state, reveal, feed), Quit);
        // Keeps the Run value pointing at this exe if it was moved, unless this
        // is a build tried out against other folders.
        if (!_paths.FromHome)
        {
            autostart.Refresh();
        }
        var restore = new Restore(state, prints, _paths.Notes);
        restore.Start();
        _screenshots = new ScreenshotFiles(state, prints, unpinning, saves, reveal);
        _screenshots.Watch(ScreenshotsFolder.Find());
        _captures.Start(_host);
        _poller = StartPoller(state, reveal, feed);
        DevBoardShot.Start(state, reveal, restore.Done);
    }

    /// <summary>What the board asks of the app, each command handed to the service that does it.</summary>
    BoardActions BoardActionsFor(
        AppState state, PendingSaves saves, OwnedFiles files, BoardFeed feed, DispatcherQueue ui,
        Prints prints, Unpinning unpinning, Reveal reveal, Notices notices, Pinning pinning)
    {
        var window = _board!.Handle;
        var commands = new PrintCommands(state, saves, prints, feed);
        return new BoardActions(
            state,
            reveal,
            prints,
            unpinning,
            commands,
            new PrintMenu(state, commands, prints, unpinning, ui, window),
            new DragOutFlow(state, saves, unpinning, feed, ui, window),
            pinning,
            new ImageDrops(prints, files, reveal, notices, _paths.Captures),
            new SnipTip(state));
    }

    /// <summary>The edge poller: reveals at the top edge, tucks when the pointer leaves, flips click-through.</summary>
    static EdgePoller StartPoller(AppState state, Reveal reveal, BoardFeed feed)
    {
        var poller = new EdgePoller(state.Gate, state.View, () => state.Settings, SnippingTool.InFront);
        poller.RevealRequested += () => reveal.Show(Tack.Core.Reveal.RevealReason.Edge);
        poller.TuckRequested += () =>
        {
            DevTrace.Write("reveal: the poller asks for a tuck");
            reveal.Tuck();
        };
        poller.ClickThroughChanged += reveal.SetClickThrough;
        poller.PointerLeft += feed.RaisePointerLeft;
        reveal.Attach(poller);
        poller.Start();
        return poller;
    }

    /// <summary>
    /// Stops taking in new prints, writes whatever save is still waiting (board.json
    /// is written a moment after each change), and ends Tack.
    /// </summary>
    void Quit()
    {
        _poller?.Dispose();
        _captures?.Dispose();
        _screenshots?.Dispose();
        _shortcuts?.Dispose();
        _storage?.Flush();
        _tray?.Dispose();
        _board?.Close();
        _host?.Dispose();
        Exit();
    }
}
