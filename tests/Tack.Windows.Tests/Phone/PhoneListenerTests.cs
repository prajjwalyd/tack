using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Tack.Core.Phone.Http;
using Tack.Core.Phone.Serving;
using Tack.Windows.Phone;

namespace Tack.Windows.Tests.Phone;

/// <summary>The listener on 127.0.0.1, on a port of its own picking, with a phone there.</summary>
public sealed class PhoneListenerTests
{
    /// <summary>Long enough that only the client ends a held connection.</summary>
    static readonly ConnectionTimeouts Holding = Loopback.Short with { Head = TimeSpan.FromSeconds(30) };

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_peer_gets_its_answer_with_every_common_header()
    {
        RequestHead? asked = null;
        PeerMatch? who = null;
        using var listener = Start(Peer, (head, _, from) =>
        {
            (asked, who) = (head, from);
            return Task.FromResult(PhoneResponse.Bytes(200, "text/html; charset=utf-8", "<p>hi</p>"u8.ToArray())
                .With("ETag", "\"0011223344556677\""));
        });

        var response = await PhoneClient.ExchangeAsync(listener.Port, "GET /?x=1 HTTP/1.1\r\nHost: my-pc:7717\r\n\r\n");

        Assert.Equal(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Content-Length: 9\r\n" +
            "Connection: close\r\n" +
            $"Content-Security-Policy: {PhoneRoutes.Csp}\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "X-Frame-Options: DENY\r\n" +
            "Referrer-Policy: no-referrer\r\n" +
            "Cross-Origin-Opener-Policy: same-origin\r\n" +
            "Cross-Origin-Resource-Policy: same-origin\r\n" +
            "Cache-Control: no-store\r\n" +
            "ETag: \"0011223344556677\"\r\n" +
            "\r\n" +
            "<p>hi</p>",
            response);
        Assert.Equal("/", asked!.Path);
        Assert.Equal("cGl4ZWw=", who!.Peer.Key);
    }

    [Fact]
    public async Task A_head_error_gets_its_status_and_nothing_else()
    {
        using var listener = Start(Peer, Unreachable);

        var response = await PhoneClient.ExchangeAsync(
            listener.Port, "POST / HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n");

        Assert.StartsWith(
            "HTTP/1.1 411 Length Required\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: 0\r\n", response);
        Assert.EndsWith("Cache-Control: no-store\r\n\r\n", response);
    }

    [Fact]
    public async Task A_client_that_stalls_gets_no_answer()
    {
        using var listener = Start(Peer, Unreachable, Loopback.Short);

        Assert.Empty(await PhoneClient.ExchangeAsync(listener.Port, "GET / HTTP/1.1\r\n"));
    }

    [Fact]
    public async Task A_handler_that_fails_ends_only_its_own_connection()
    {
        var calls = 0;
        using var listener = Start(Peer, (_, _, _) => Interlocked.Increment(ref calls) == 1
            ? throw new InvalidOperationException("boom")
            : Task.FromResult(PhoneResponse.Empty(404)));

        Assert.Empty(await PhoneClient.ExchangeAsync(listener.Port, "GET / HTTP/1.1\r\n\r\n"));
        var next = await PhoneClient.ExchangeAsync(listener.Port, "GET / HTTP/1.1\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 404 Not Found\r\n", next);
    }

    [Fact]
    public async Task A_stranger_is_closed_before_a_byte_is_read()
    {
        IPAddress? looked = null;
        var handled = false;
        using var listener = Start(
            ip =>
            {
                looked = ip;
                return Task.FromResult<PeerMatch?>(null);
            },
            (_, _, _) =>
            {
                handled = true;
                return Task.FromResult(PhoneResponse.Empty(200));
            },
            Holding);

        // Nothing is sent: a server that read first would wait out its 30 s head timeout.
        using var socket = await PhoneClient.ConnectAsync(listener.Port);
        var clock = Stopwatch.StartNew();
        Assert.Empty(await PhoneClient.ReadToEndAsync(socket));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4));
        Assert.Equal(IPAddress.Loopback, looked);
        Assert.Empty(await PhoneClient.ExchangeAsync(listener.Port, "GET / HTTP/1.1\r\n\r\n"));
        Assert.False(handled);
    }

    [Fact]
    public async Task The_seventeenth_connection_is_closed_at_once()
    {
        var looked = 0;
        using var listener = Start(
            _ =>
            {
                Interlocked.Increment(ref looked);
                return Peer(IPAddress.Loopback);
            },
            (_, _, _) => Task.FromResult(PhoneResponse.Empty(404)),
            Holding);
        var held = new List<Socket>();
        try
        {
            for (var n = 0; n < PhoneListener.MaxConnections; n++)
            {
                held.Add(await PhoneClient.ConnectAsync(listener.Port));
            }
            await Until(() => Volatile.Read(ref looked) == PhoneListener.MaxConnections);

            using (var extra = await PhoneClient.ConnectAsync(listener.Port))
            {
                Assert.Empty(await PhoneClient.ReadToEndAsync(extra));
            }
            Assert.Equal(PhoneListener.MaxConnections, Volatile.Read(ref looked));
        }
        finally
        {
            held.ForEach(socket => socket.Dispose());
        }

        // The held ones closing frees their places.
        await Until(async () => (await PhoneClient.ExchangeAsync(listener.Port, "GET / HTTP/1.1\r\n\r\n")).Length > 0);
    }

    [Fact]
    public void A_port_in_use_says_so()
    {
        var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        try
        {
            var port = ((IPEndPoint)taken.LocalEndpoint).Port;

            var refused = Assert.Throws<PhoneServeException>(() => Start(Peer, Unreachable, port: port));

            Assert.Equal($"port {port} is in use", refused.Message);
        }
        finally
        {
            taken.Stop();
        }
    }

    [Fact]
    public async Task Disposing_closes_the_port()
    {
        int port;
        using (var listener = Start(Peer, Unreachable))
        {
            port = listener.Port;
        }

        await Assert.ThrowsAsync<SocketException>(() => PhoneClient.ConnectAsync(port));
        using var again = Start(Peer, Unreachable, port: port);
        Assert.Equal(port, again.Port);
    }

    static Task<PeerMatch?> Peer(IPAddress ip) => Task.FromResult<PeerMatch?>(PhoneClient.Match);

    static Task<PhoneResponse> Unreachable(RequestHead head, IRequestBody body, PeerMatch from) =>
        throw new InvalidOperationException("No request should reach the routes.");

    static PhoneListener Start(
        Func<IPAddress, Task<PeerMatch?>> peerAt, PhoneHandler handle, ConnectionTimeouts? timeouts = null, int port = 0) =>
        PhoneListener.Start(IPAddress.Loopback, port, peerAt, handle, PhoneRoutes.Csp, timeouts ?? Loopback.Short);

    static Task Until(Func<bool> done) => Until(() => Task.FromResult(done()));

    /// <summary>Waits for something the server does on its own threads, failing after the client's patience.</summary>
    static async Task Until(Func<Task<bool>> done)
    {
        var clock = Stopwatch.StartNew();
        while (!await done())
        {
            Assert.True(clock.Elapsed < PhoneClient.Patience, "The server did not get there in time.");
            await Task.Delay(20, Cancel);
        }
    }
}
