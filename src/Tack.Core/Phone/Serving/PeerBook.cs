using System.Net;
using Tack.Core.NetBird;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// NetBird's peers as the phone server knows them: the status it started with,
/// looked up again when a request needs a fresher one. An address NetBird does
/// not list may make Tack ask NetBird again at most every
/// <see cref="UnknownRecheck"/> (a peer that joined since the last look); an
/// API request wants a list no older than <see cref="FreshFor"/>, so an
/// address freed by one peer and given to another is seen within that. One
/// caller asks NetBird at a time, without holding the lock; the others go on
/// with what is known. A look that finds this PC at another address is
/// ignored: the server is restarted for that. Safe on any thread.
/// </summary>
public sealed class PeerBook
{
    public static readonly TimeSpan UnknownRecheck = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(10);

    readonly Func<Task<NetBirdStatus?>> _look;
    readonly TimeProvider _time;
    readonly Lock _gate = new();

    NetBirdStatus _status;

    /// <summary>When <see cref="_status"/> was last looked up, as a timestamp of the clock.</summary>
    long _lookedAt;

    /// <summary>1 while a caller is asking NetBird.</summary>
    int _refreshing;

    /// <param name="status">This PC's status when the server started.</param>
    /// <param name="look">Asks NetBird for the status now (<c>netbird status --json</c>); null when it cannot say.</param>
    /// <param name="time">The clock the ages are measured on.</param>
    public PeerBook(NetBirdStatus status, Func<Task<NetBirdStatus?>> look, TimeProvider time)
    {
        (_status, _look, _time) = (status, look, time);
        _lookedAt = time.GetTimestamp();
    }

    /// <summary>The status as last looked up.</summary>
    public NetBirdStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>
    /// The peer at <paramref name="ip"/> with this PC's status, if NetBird lists
    /// one there; <paramref name="fresh"/> asks for a list no older than
    /// <see cref="FreshFor"/>.
    /// </summary>
    public async Task<PeerMatch?> FindAsync(IPAddress ip, bool fresh)
    {
        bool known;
        TimeSpan age;
        lock (_gate)
        {
            (known, age) = (_status.PeerAt(ip) != null, _time.GetElapsedTime(_lookedAt));
        }
        var stale = known ? fresh && age >= FreshFor : age >= UnknownRecheck;
        if (stale && Interlocked.Exchange(ref _refreshing, 1) == 0)
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        lock (_gate)
        {
            return _status.PeerAt(ip) is { } peer ? new PeerMatch(_status, peer) : null;
        }
    }

    /// <summary>A fresh look from elsewhere (the watcher); ignored if it puts this PC at another address.</summary>
    public void Update(NetBirdStatus status)
    {
        lock (_gate)
        {
            if (status.Ip.Equals(_status.Ip))
            {
                (_status, _lookedAt) = (status, _time.GetTimestamp());
            }
        }
    }

    /// <summary>
    /// Asks NetBird, outside the lock. The age starts again even when NetBird
    /// says nothing, or puts this PC elsewhere, so a stray address cannot make
    /// Tack ask over and over.
    /// </summary>
    async Task RefreshAsync()
    {
        NetBirdStatus? looked = null;
        try
        {
            looked = await _look().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _lookedAt = _time.GetTimestamp();
                if (looked != null && looked.Ip.Equals(_status.Ip))
                {
                    _status = looked;
                }
            }
            Volatile.Write(ref _refreshing, 0);
        }
    }
}
