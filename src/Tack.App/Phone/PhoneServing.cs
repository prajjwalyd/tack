using Tack.Core.Diagnostics;
using Tack.Core.NetBird;
using Tack.Core.Phone;
using Tack.Core.Phone.Serving;
using Tack.Windows.NetBird;

namespace Tack.App.Phone;

/// <summary>
/// Keeps the phone server in step with the switch and NetBird: serving on this
/// PC's NetBird address while the switch is on and NetBird is connected, and
/// stopping when NetBird goes or moves this PC to another address
/// (<see cref="ServingPlan"/>). Also says how NetBird stands, and why the
/// board could not be served. Safe on any thread.
/// </summary>
/// <remarks>
/// Each look is a reconcile on the thread pool, one at a time: nudges during
/// one make it run once more (<see cref="OneAtATime"/>). While the switch is
/// on it holds a <see cref="NetworkWatch"/>, whose changes nudge it. Its lock
/// is never held while waiting on NetBird, the server or the app's own lock.
/// </remarks>
/// <param name="changed">Something it shows changed. Called on the thread pool.</param>
/// <param name="stopped">A server stopped, so the requests waiting on it go. Called on the thread pool.</param>
sealed class PhoneServing(SettingsChanges settings, Action changed, Action stopped) : IDisposable
{
    readonly Lock _lock = new();
    IPhoneHost? _host;
    OneAtATime? _reconcile;
    PhoneServer? _server;
    NetworkWatch? _watch;

    /// <summary>Taken as connected until a look says otherwise, as Settings shows it from board.json alone.</summary>
    NetBirdState _netBird = NetBirdState.Connected;
    string? _error;
    bool _looking;
    bool _disposed;

    /// <summary>Read the first time a server starts. Reconciles only, which run one at a time.</summary>
    PhoneFiles? _files;

    /// <summary>How NetBird stands, where the board is served (null while it is not), and why it could not be.</summary>
    public (NetBirdState NetBird, string? Address, string? Error) Now
    {
        get
        {
            lock (_lock)
            {
                return (_netBird, _server?.Address, _error);
            }
        }
    }

    /// <summary>From now on the routes get <paramref name="host"/>, and nudges reconcile. Once.</summary>
    public void Start(IPhoneHost host)
    {
        _host = host;
        _reconcile = new OneAtATime("phone: reconcile", ReconcileAsync);
    }

    /// <summary>Looks at the switch and NetBird again, in the background.</summary>
    public void Nudge() => _reconcile?.Nudge();

    /// <summary>
    /// With the switch off, looks at NetBird in the background, so the page can
    /// say what turning it on needs; one look at a time. A change it finds is told.
    /// </summary>
    public void Look()
    {
        lock (_lock)
        {
            if (_looking || _disposed)
            {
                return;
            }
            _looking = true;
        }
        Background.Run("phone: looking at NetBird", async () =>
        {
            var found = false;
            try
            {
                var now = NetBirdStates.Of(await NetBirdCli.StatusAsync().ConfigureAwait(false), NetBirdCli.Installed);
                // Once the switch is on, the reconciles say how NetBird is.
                var off = !settings.Current.Phone.On;
                lock (_lock)
                {
                    if (off && now != _netBird)
                    {
                        (_netBird, found) = (now, true);
                    }
                }
            }
            finally
            {
                lock (_lock)
                {
                    _looking = false;
                }
            }
            if (found)
            {
                changed();
            }
        });
    }

    /// <summary>Stops serving and watching the network, for good: Tack is quitting.</summary>
    public void Dispose()
    {
        PhoneServer? server;
        NetworkWatch? watch;
        lock (_lock)
        {
            _disposed = true;
            (server, watch, _server, _watch) = (_server, _watch, null, null);
        }
        watch?.Dispose();
        server?.Dispose();
    }

    /// <summary>Serves or stops serving to match the switch and NetBird, which is asked only while the switch is on.</summary>
    async Task ReconcileAsync()
    {
        var on = settings.Current.Phone.On;
        HoldWatch(on);
        var status = on ? await NetBirdCli.StatusAsync().ConfigureAwait(false) : null;
        NetBirdState? netBird = on ? NetBirdStates.Of(status, NetBirdCli.Installed) : null;
        var shown = false;
        PhoneServer? gone = null, kept = null;
        ServingPlan plan;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            if (netBird is { } now && now != _netBird)
            {
                (_netBird, shown) = (now, true);
            }
            plan = ServingPlan.For(_server?.Ip, status);
            if (plan.Stop)
            {
                (gone, _server, shown) = (_server, null, true);
            }
            kept = plan.Keep ? _server : null;
            if (!on && _error is not null)
            {
                (_error, shown) = (null, true);
            }
        }
        if (gone is not null)
        {
            gone.Dispose();
            stopped();
        }
        if (status is not null)
        {
            kept?.Update(status);
            if (plan.Start)
            {
                shown |= Serve(status);
            }
        }
        if (shown)
        {
            changed();
        }
    }

    /// <summary>Starts a server on <paramref name="status"/>'s address, or keeps why it could not. False if Tack is quitting.</summary>
    bool Serve(NetBirdStatus status)
    {
        PhoneServer? server = null;
        string? error = null;
        try
        {
            server = PhoneServer.Start(status, _host!, _files ??= PhonePageFiles.Read());
        }
        catch (Exception e)
        {
            DevTrace.Write($"phone: cannot serve the board: {e}");
            error = $"Couldn't start: {e.Message}";
        }
        lock (_lock)
        {
            if (!_disposed)
            {
                (_server, _error) = (server, error);
                return true;
            }
        }
        server?.Dispose();
        return false;
    }

    /// <summary>Holds the network watch while the switch is on, and lets it go when it is off.</summary>
    void HoldWatch(bool on)
    {
        NetworkWatch? dropped = null;
        lock (_lock)
        {
            if (on && _watch is null && !_disposed)
            {
                _watch = new NetworkWatch();
                _watch.Changed += Nudge;
            }
            else if (!on)
            {
                (dropped, _watch) = (_watch, null);
            }
        }
        dropped?.Dispose();
    }
}
