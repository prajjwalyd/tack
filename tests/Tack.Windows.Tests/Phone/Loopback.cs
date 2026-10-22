using System.Net;
using System.Net.Sockets;
using System.Text;
using Tack.Windows.Phone;

namespace Tack.Windows.Tests.Phone;

/// <summary>
/// One connection over loopback: the server's end as a <see cref="PhoneConnection"/>,
/// and the client's end, which stays open until the test is done, so the server
/// sees no close until the client says so.
/// </summary>
sealed class Loopback : IDisposable
{
    /// <summary>Timeouts short enough that a test sees a stall in moments.</summary>
    public static readonly ConnectionTimeouts Short = new(
        Head: TimeSpan.FromMilliseconds(300), BodyIdle: TimeSpan.FromMilliseconds(300),
        BodyTotal: TimeSpan.FromSeconds(3), Write: TimeSpan.FromSeconds(3));

    Loopback(Socket client, PhoneConnection server)
    {
        (Client, Server) = (client, server);
    }

    public Socket Client { get; }

    public PhoneConnection Server { get; }

    /// <summary>A connection on which the client has already sent <paramref name="request"/>.</summary>
    public static Task<Loopback> OpenAsync(string request, ConnectionTimeouts? timeouts = null) =>
        OpenAsync(Encoding.UTF8.GetBytes(request), timeouts);

    public static async Task<Loopback> OpenAsync(byte[] request, ConnectionTimeouts? timeouts = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var client = await PhoneClient.ConnectAsync(((IPEndPoint)listener.LocalEndpoint).Port);
            var accepted = await listener.AcceptSocketAsync();
            if (request.Length > 0)
            {
                await client.SendAsync(request);
            }
            return new Loopback(client, new PhoneConnection(accepted, timeouts ?? ConnectionTimeouts.Default));
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Closes the server's end, then reads all the client received.</summary>
    public async Task<byte[]> ReceivedAsync()
    {
        Server.Dispose();
        return await PhoneClient.ReadToEndAsync(Client);
    }

    public void Dispose()
    {
        Server.Dispose();
        Client.Dispose();
    }
}
