using System.Net;
using Tack.Core.Diagnostics;
using Tack.Core.NetBird;
using Tack.Core.Phone.Serving;
using Tack.Windows.NetBird;
using Tack.Windows.Phone;

namespace Tack.App.Phone;

/// <summary>
/// The phone board being served: the listener on this PC's NetBird address,
/// with its own peer book and pin limits, so a server started again begins
/// afresh. Disposing it closes the port.
/// </summary>
sealed class PhoneServer : IDisposable
{
    readonly PeerBook _peers;
    readonly PhoneListener _listener;

    PhoneServer(NetBirdStatus status, PeerBook peers, PhoneListener listener)
    {
        (Ip, Address, _peers, _listener) = (status.Ip, PhoneAddress.Of(status), peers, listener);
    }

    /// <summary>The NetBird address it listens on.</summary>
    public IPAddress Ip { get; }

    /// <summary>What a phone opens: "http://my-pc.netbird.cloud:7717".</summary>
    public string Address { get; }

    /// <summary>
    /// Serves the board on <paramref name="status"/>'s address, on
    /// <see cref="PhoneAddress.Port"/>. Throws <see cref="PhoneServeException"/>
    /// when it cannot listen there.
    /// </summary>
    public static PhoneServer Start(NetBirdStatus status, IPhoneHost host, PhoneFiles files)
    {
        var peers = new PeerBook(status, NetBirdCli.StatusAsync, TimeProvider.System);
        var routes = new PhoneRoutes(host, files, peers, new PinLimits(TimeProvider.System));
        var listener = PhoneListener.Start(
            status.Ip, PhoneAddress.Port, ip => peers.FindAsync(ip, fresh: false), routes.HandleAsync, PhoneRoutes.Csp);
        var server = new PhoneServer(status, peers, listener);
        DevTrace.Write($"phone: serving {server.Address}");
        return server;
    }

    /// <summary>A fresh look at NetBird, for the peers the routes ask about.</summary>
    public void Update(NetBirdStatus status) => _peers.Update(status);

    public void Dispose()
    {
        _listener.Dispose();
        DevTrace.Write($"phone: stopped serving {Address}");
    }
}
