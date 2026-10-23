using System.Net;
using System.Net.Sockets;
using System.Text;
using Tack.Core.NetBird;
using Tack.Core.Phone.Serving;

namespace Tack.Windows.Tests.Phone;

/// <summary>A phone on loopback: the peer NetBird would list there, and a plain socket to talk to the server with.</summary>
static class PhoneClient
{
    /// <summary>How long a test waits for anything from the server before it fails.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>A phone at 127.0.0.1, as NetBird would list it to this PC.</summary>
    public static readonly PeerMatch Match = new(
        new NetBirdStatus(
            IPAddress.Loopback, "my-pc.netbird.cloud", [new Peer(IPAddress.Loopback, "pixel.netbird.cloud", "cGl4ZWw=")]),
        new Peer(IPAddress.Loopback, "pixel.netbird.cloud", "cGl4ZWw="));

    /// <summary>A socket connected to the server on <paramref name="port"/>.</summary>
    public static async Task<Socket> ConnectAsync(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, port);
        return socket;
    }

    /// <summary>Sends <paramref name="request"/> on a new connection and reads the whole answer.</summary>
    public static async Task<string> ExchangeAsync(int port, string request)
    {
        using var socket = await ConnectAsync(port);
        await socket.SendAsync(Encoding.UTF8.GetBytes(request));
        return Encoding.UTF8.GetString(await ReadToEndAsync(socket));
    }

    /// <summary>
    /// Everything the server sends until it closes; a reset or an abort ends it
    /// as a close does (a connection closed with the request unread can come
    /// back either way, by timing).
    /// </summary>
    public static async Task<byte[]> ReadToEndAsync(Socket socket)
    {
        using var output = new MemoryStream();
        using var deadline = new CancellationTokenSource(Patience);
        var chunk = new byte[64 * 1024];
        while (true)
        {
            int read;
            try
            {
                read = await socket.ReceiveAsync(chunk, SocketFlags.None, deadline.Token);
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
            {
                read = 0;
            }
            if (read == 0)
            {
                return output.ToArray();
            }
            output.Write(chunk, 0, read);
        }
    }
}
