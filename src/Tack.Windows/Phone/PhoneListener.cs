using System.Net;
using System.Net.Sockets;
using Tack.Core.Diagnostics;
using Tack.Core.Phone.Http;
using Tack.Core.Phone.Serving;

namespace Tack.Windows.Phone;

/// <summary>
/// The phone board's listener, on this PC's NetBird address only. A
/// connection from anything NetBird does not list as a peer is closed before
/// a byte is read; WireGuard binds each peer's address to its key, so the
/// address really is that peer. At most <see cref="MaxConnections"/> are open
/// at once (more are closed at once), every read and write has a timeout, and
/// each connection carries one request. Plain HTTP: the WireGuard tunnel
/// encrypts it end to end, and NetBird gives peers no certificates.
/// </summary>
/// <remarks>
/// Connections are served on the thread pool with async sockets, not a thread
/// each, so stalled ones cost a little memory and nothing else.
/// </remarks>
public sealed class PhoneListener : IDisposable
{
    /// <summary>Connections open at once; more are closed at once. A phone uses a few.</summary>
    public const int MaxConnections = 16;

    readonly TcpListener _listener;
    readonly Func<IPAddress, Task<PeerMatch?>> _peerAt;
    readonly PhoneHandler _handle;
    readonly string _csp;
    readonly ConnectionTimeouts _timeouts;
    int _open;
    int _stopped;

    PhoneListener(
        TcpListener listener, Func<IPAddress, Task<PeerMatch?>> peerAt, PhoneHandler handle, string csp,
        ConnectionTimeouts timeouts)
    {
        (_listener, _peerAt, _handle, _csp, _timeouts) = (listener, peerAt, handle, csp, timeouts);
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The port it listens on: <see cref="PhoneAddress.Port"/>, or the one picked for port 0 in tests.</summary>
    public int Port { get; }

    /// <summary>
    /// Listens on <paramref name="ip"/>:<paramref name="port"/> and answers each
    /// peer's request with <paramref name="handle"/>, sending every response with
    /// the headers all responses carry and <paramref name="csp"/> as its
    /// Content-Security-Policy. <paramref name="peerAt"/> names the peer at a
    /// connection's address, or null for one NetBird does not list (in the app,
    /// <c>PeerBook.FindAsync(ip, fresh: false)</c>). Throws
    /// <see cref="PhoneServeException"/> when it cannot listen.
    /// </summary>
    public static PhoneListener Start(
        IPAddress ip, int port, Func<IPAddress, Task<PeerMatch?>> peerAt, PhoneHandler handle, string csp) =>
        Start(ip, port, peerAt, handle, csp, ConnectionTimeouts.Default);

    /// <summary>As the public <c>Start</c>, with the timeouts given: tests shorten them.</summary>
    internal static PhoneListener Start(
        IPAddress ip, int port, Func<IPAddress, Task<PeerMatch?>> peerAt, PhoneHandler handle, string csp,
        ConnectionTimeouts timeouts)
    {
        var listener = new TcpListener(ip, port);
        try
        {
            listener.Start();
        }
        catch (SocketException e)
        {
            listener.Stop();
            var why = e.SocketErrorCode == SocketError.AddressAlreadyInUse ? $"port {port} is in use" : e.Message;
            throw new PhoneServeException(why, e);
        }
        var serving = new PhoneListener(listener, peerAt, handle, csp, timeouts);
        _ = Task.Run(serving.AcceptAsync);
        DevTrace.Write($"phone: listening on {ip}:{serving.Port}");
        return serving;
    }

    /// <summary>Stops listening: the port closes; requests under way finish or time out.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            // Ends the accept under way, which ends the loop.
            _listener.Stop();
            DevTrace.Write($"phone: stopped listening on port {Port}");
        }
    }

    async Task AcceptAsync()
    {
        while (Volatile.Read(ref _stopped) == 0)
        {
            Socket socket;
            try
            {
                socket = await _listener.AcceptSocketAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // Stopping, or a client that reset before it was accepted.
                continue;
            }
            if (Interlocked.Increment(ref _open) > MaxConnections)
            {
                Interlocked.Decrement(ref _open);
                socket.Dispose();
                continue;
            }
            _ = Task.Run(() => ServeAsync(socket));
        }
    }

    /// <summary>One connection, start to end. Nothing it does may throw out of it.</summary>
    async Task ServeAsync(Socket socket)
    {
        try
        {
            using var connection = new PhoneConnection(socket, _timeouts);
            await ServeAsync(socket, connection).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            DevTrace.Write($"phone: a connection failed: {e.Message}");
        }
        finally
        {
            socket.Dispose();
            Interlocked.Decrement(ref _open);
        }
    }

    async Task ServeAsync(Socket socket, PhoneConnection connection)
    {
        if (socket.RemoteEndPoint is not IPEndPoint { Address: var address })
        {
            return;
        }
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (address.AddressFamily != AddressFamily.InterNetwork
            || await _peerAt(address).ConfigureAwait(false) is not { } from)
        {
            DevTrace.Write($"phone: closed a connection from {address}, not a NetBird peer");
            return;
        }
        socket.NoDelay = true;
        var read = await connection.ReadHeadAsync().ConfigureAwait(false);
        PhoneResponse response;
        if (read.Head is { } head)
        {
            response = await _handle(head, connection, from).ConfigureAwait(false);
        }
        else if (read.Error?.Status() is { } status)
        {
            response = PhoneResponse.Empty(status);
        }
        else
        {
            return;
        }
        await connection.SendAsync(response, _csp).ConfigureAwait(false);
    }
}
