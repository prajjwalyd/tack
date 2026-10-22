using System.Net;
using System.Net.Sockets;
using System.Text;
using Tack.Core.NetBird;
using Tack.Core.Phone.Serving;
using Tack.Windows.Phone;

namespace Tack.Windows.Tests.Phone;

/// <summary>
/// The listener and the routes together, as the app runs them, over loopback
/// addresses standing in for NetBird's: this PC at 127.77.17.1 on the board's
/// port, a phone NetBird lists at 127.77.17.2, and a stranger at 127.77.17.3.
/// Each request is sent from its device's own address, as the page sends it.
/// </summary>
public sealed class PhoneEndToEndTests : IDisposable
{
    const string HostName = "my-pc.netbird.test:7717";

    static readonly IPAddress Me = IPAddress.Parse("127.77.17.1");
    static readonly IPAddress Phone = IPAddress.Parse("127.77.17.2");
    static readonly IPAddress Stranger = IPAddress.Parse("127.77.17.3");

    readonly GatedHost _host = new();
    readonly PhoneListener _listener;

    public PhoneEndToEndTests()
    {
        var status = new NetBirdStatus(Me, "my-pc.netbird.test", [new Peer(Phone, "pixel.netbird.test", "phone-key")]);
        var peers = new PeerBook(status, () => Task.FromResult<NetBirdStatus?>(status), TimeProvider.System);
        var files = new PhoneFiles("<html>"u8.ToArray(), "js"u8.ToArray(), "css"u8.ToArray(), [0x89]);
        var routes = new PhoneRoutes(_host, files, peers, new PinLimits(TimeProvider.System));
        _listener = PhoneListener.Start(
            Me, PhoneAddress.Port, ip => peers.FindAsync(ip, fresh: false), routes.HandleAsync, PhoneRoutes.Csp);
    }

    [Fact]
    public async Task The_page_loads_with_its_headers()
    {
        var reply = await SendAsync(Phone, "GET / HTTP/1.1\r\nHost: " + HostName + "\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 200 OK\r\n", reply);
        Assert.Contains("Content-Security-Policy: " + PhoneRoutes.Csp + "\r\n", reply);
        Assert.Contains("Cache-Control: no-store\r\n", reply);
        Assert.EndsWith("\r\n\r\n<html>", reply);
    }

    [Fact]
    public async Task A_new_phone_is_asked_about_then_sees_the_board_once_allowed()
    {
        var board = "GET /api/board HTTP/1.1\r\nHost: " + HostName + "\r\nX-Tack: 1\r\n\r\n";

        var waiting = await SendAsync(Phone, board);
        Assert.StartsWith("HTTP/1.1 403 Forbidden\r\n", waiting);
        Assert.Contains("\"state\":\"waiting\"", waiting);
        Assert.Contains("\"code\":\"4821\"", waiting);
        Assert.Equal("pixel", Assert.Single(_host.Pending).Name);

        _host.Answer("phone-key", allow: true);
        var shown = await SendAsync(Phone, board);
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", shown);
        Assert.EndsWith("{\"pc\":\"my-pc\",\"you\":\"pixel\",\"prints\":[]}", shown);

        var tag = Header(shown, "ETag");
        var same = await SendAsync(Phone, board.Replace("\r\n\r\n", $"\r\nIf-None-Match: {tag}\r\n\r\n"));
        Assert.StartsWith("HTTP/1.1 304 Not Modified\r\n", same);
    }

    [Fact]
    public async Task An_allowed_phone_pins_text()
    {
        await SendAsync(Phone, "GET /api/board HTTP/1.1\r\nHost: " + HostName + "\r\nX-Tack: 1\r\n\r\n");
        _host.Answer("phone-key", allow: true);

        var reply = await SendAsync(Phone,
            "POST /api/pin HTTP/1.1\r\nHost: " + HostName + "\r\nX-Tack: 1\r\nContent-Type: text/plain\r\n" +
            "Content-Length: 5\r\n\r\nhello");

        Assert.StartsWith("HTTP/1.1 200 OK\r\n", reply);
        Assert.EndsWith("Pinned", reply);
        Assert.Equal(["hello"], _host.PinnedTexts);
    }

    [Fact]
    public async Task Another_name_in_Host_is_misdirected()
    {
        var reply = await SendAsync(Phone, "GET / HTTP/1.1\r\nHost: evil.example:7717\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 421 Misdirected Request\r\n", reply);
    }

    [Fact]
    public async Task A_stranger_is_closed_without_an_answer()
    {
        var reply = await SendAsync(Stranger, "GET / HTTP/1.1\r\nHost: " + HostName + "\r\n\r\n");

        Assert.Equal(string.Empty, reply);
    }

    public void Dispose() => _listener.Dispose();

    /// <summary>Sends <paramref name="request"/> from <paramref name="from"/> and reads the whole answer.</summary>
    static async Task<string> SendAsync(IPAddress from, string request)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(from, 0));
        await socket.ConnectAsync(new IPEndPoint(Me, PhoneAddress.Port));
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(request));
        }
        catch (SocketException)
        {
            // Closed before the request went: a stranger.
        }
        var answer = new MemoryStream();
        var buffer = new byte[4096];
        try
        {
            int read;
            while ((read = await socket.ReceiveAsync(buffer)) > 0)
            {
                answer.Write(buffer, 0, read);
            }
        }
        catch (SocketException)
        {
            // Reset rather than closed: nothing more was sent.
        }
        return Encoding.UTF8.GetString(answer.ToArray());
    }

    static string Header(string reply, string name) =>
        reply.Split("\r\n").First(l => l.StartsWith(name + ": ", StringComparison.Ordinal))[(name.Length + 2)..];
}
