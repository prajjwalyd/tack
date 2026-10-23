using System.Net.Sockets;
using System.Text;
using Tack.Core.Phone.Http;
using Tack.Windows.Phone;

namespace Tack.Windows.Tests.Phone;

/// <summary>One connection over loopback, as the server reads and answers it.</summary>
public sealed class PhoneConnectionTests
{
    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_a_head_and_its_body()
    {
        using var pair = await Loopback.OpenAsync(
            "POST /api/pin?v=1 HTTP/1.1\r\nHost: pc:7717\r\nContent-Length: 5\r\nX-Tack: 1\r\n\r\nhello");

        var head = (await pair.Server.ReadHeadAsync()).Head!;

        Assert.Equal(("POST", "/api/pin"), (head.Method, head.Path));
        Assert.Equal("1", head.Header("x-tack"));
        Assert.Equal("hello"u8.ToArray(), await pair.Server.ReadAsync(10));
    }

    [Fact]
    public async Task A_body_over_the_cap_is_never_read()
    {
        using var pair = await Loopback.OpenAsync("POST / HTTP/1.1\r\nContent-Length: 1000000000000\r\n\r\n");

        Assert.NotNull((await pair.Server.ReadHeadAsync()).Head);
        Assert.Null(await pair.Server.ReadAsync(1024));
    }

    [Fact]
    public async Task A_head_sent_in_pieces_is_read_whole()
    {
        using var pair = await Loopback.OpenAsync("GET /phone.css HT");
        var reading = pair.Server.ReadHeadAsync();
        await Task.Delay(50, Cancel);
        await pair.Client.SendAsync("TP/1.1\r\nHost: pc\r\n\r\n"u8.ToArray(), Cancel);

        Assert.Equal("/phone.css", (await reading).Head!.Path);
    }

    [Fact]
    public async Task A_head_that_stalls_is_gone()
    {
        using var pair = await Loopback.OpenAsync("GET / HTTP/1.1\r\nHost: pc\r\n", Loopback.Short);

        Assert.Equal(HeadError.Gone, (await pair.Server.ReadHeadAsync()).Error);
    }

    [Fact]
    public async Task A_client_that_closes_before_its_head_is_gone()
    {
        using var pair = await Loopback.OpenAsync("GET / HT");
        pair.Client.Shutdown(SocketShutdown.Send);

        Assert.Equal(HeadError.Gone, (await pair.Server.ReadHeadAsync()).Error);
    }

    [Fact]
    public async Task A_body_that_stalls_is_null()
    {
        using var pair = await Loopback.OpenAsync("POST / HTTP/1.1\r\nContent-Length: 10\r\n\r\nabc", Loopback.Short);
        await pair.Server.ReadHeadAsync();

        Assert.Null(await pair.Server.ReadAsync(100));
    }

    [Fact]
    public async Task A_body_that_trickles_past_its_total_time_is_null()
    {
        var slow = Loopback.Short with { BodyIdle = TimeSpan.FromSeconds(3), BodyTotal = TimeSpan.FromMilliseconds(400) };
        using var pair = await Loopback.OpenAsync("POST / HTTP/1.1\r\nContent-Length: 100\r\n\r\n", slow);
        await pair.Server.ReadHeadAsync();
        var reading = pair.Server.ReadAsync(100);
        for (var n = 0; n < 8 && !reading.IsCompleted; n++)
        {
            await pair.Client.SendAsync("x"u8.ToArray(), Cancel);
            await Task.Delay(100, Cancel);
        }

        Assert.Null(await reading);
    }

    [Fact]
    public async Task A_body_cut_short_is_null()
    {
        using var pair = await Loopback.OpenAsync("POST / HTTP/1.1\r\nContent-Length: 10\r\n\r\nabc");
        await pair.Server.ReadHeadAsync();
        pair.Client.Shutdown(SocketShutdown.Send);

        Assert.Null(await pair.Server.ReadAsync(100));
    }

    [Fact]
    public async Task Early_bytes_past_the_length_are_dropped()
    {
        using var pair = await Loopback.OpenAsync("POST / HTTP/1.1\r\nContent-Length: 3\r\n\r\nabcdef");
        await pair.Server.ReadHeadAsync();

        Assert.Equal("abc"u8.ToArray(), await pair.Server.ReadAsync(100));
    }

    [Fact]
    public async Task A_body_is_read_once()
    {
        using var pair = await Loopback.OpenAsync("POST / HTTP/1.1\r\nContent-Length: 2\r\n\r\nok");
        await pair.Server.ReadHeadAsync();

        Assert.NotNull(await pair.Server.ReadAsync(100));
        Assert.Null(await pair.Server.ReadAsync(100));
    }

    [Fact]
    public async Task A_client_that_expects_100_continue_is_told_to_go_on()
    {
        using var pair = await Loopback.OpenAsync(
            "POST /api/pin HTTP/1.1\r\nContent-Length: 5\r\nExpect: 100-Continue\r\n\r\n");
        await pair.Server.ReadHeadAsync();
        var reading = pair.Server.ReadAsync(100);

        var interim = new byte[ResponseHead.Continue.Length];
        var got = 0;
        while (got < interim.Length)
        {
            got += await pair.Client.ReceiveAsync(interim.AsMemory(got), Cancel);
        }
        Assert.Equal(ResponseHead.Continue, Encoding.ASCII.GetString(interim));
        await pair.Client.SendAsync("hello"u8.ToArray(), Cancel);

        Assert.Equal("hello"u8.ToArray(), await reading);
    }

    [Fact]
    public async Task A_body_over_the_cap_is_not_told_to_go_on()
    {
        using var pair = await Loopback.OpenAsync("POST / HTTP/1.1\r\nContent-Length: 5\r\nExpect: 100-continue\r\n\r\n");
        await pair.Server.ReadHeadAsync();

        Assert.Null(await pair.Server.ReadAsync(4));
        Assert.Empty(await pair.ReceivedAsync());
    }

    [Fact]
    public async Task A_file_body_is_exactly_its_length()
    {
        using var scratch = new Scratch("phone-file");
        var bytes = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 7)).ToArray();
        var path = scratch.File("print.png");
        await File.WriteAllBytesAsync(path, bytes, Cancel);
        using var pair = await Loopback.OpenAsync("GET /api/print/1 HTTP/1.1\r\n\r\n");
        await pair.Server.ReadHeadAsync();

        // Read as it is sent: more than the socket buffers would stall the send.
        var receiving = PhoneClient.ReadToEndAsync(pair.Client);
        Assert.True(await pair.Server.SendAsync(PhoneResponse.File("image/png", path, 150_000), "default-src 'none'"));
        pair.Server.Dispose();
        var (head, body) = Split(await receiving);

        Assert.StartsWith("HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: 150000\r\n", head);
        Assert.Equal(bytes.AsSpan(0, 150_000).ToArray(), body);
    }

    [Fact]
    public async Task A_file_that_is_gone_is_not_found()
    {
        using var scratch = new Scratch("phone-file");
        using var pair = await Loopback.OpenAsync("GET /api/print/1 HTTP/1.1\r\n\r\n");
        await pair.Server.ReadHeadAsync();

        await pair.Server.SendAsync(PhoneResponse.File("image/png", scratch.File("gone.png"), 10), "default-src 'none'");
        var (head, body) = Split(await pair.ReceivedAsync());

        Assert.StartsWith("HTTP/1.1 404 Not Found\r\n", head);
        Assert.Contains("\r\nContent-Length: 0\r\n", head);
        Assert.Empty(body);
    }

    [Fact]
    public async Task Bytes_follow_the_head()
    {
        using var pair = await Loopback.OpenAsync("GET / HTTP/1.1\r\n\r\n");
        await pair.Server.ReadHeadAsync();

        await pair.Server.SendAsync(PhoneResponse.Text(200, "Pinned"), "default-src 'none'");
        var (head, body) = Split(await pair.ReceivedAsync());

        Assert.Equal(ResponseHead.Text(PhoneResponse.Text(200, "Pinned"), "default-src 'none'"), head);
        Assert.Equal("Pinned"u8.ToArray(), body);
    }

    /// <summary>A response's head (with its blank line) and its body.</summary>
    static (string Head, byte[] Body) Split(byte[] response)
    {
        var end = response.AsSpan().IndexOf("\r\n\r\n"u8) + 4;
        return (Encoding.UTF8.GetString(response, 0, end), response[end..]);
    }
}
