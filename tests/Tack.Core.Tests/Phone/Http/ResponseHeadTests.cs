using Tack.Core.Phone.Http;

namespace Tack.Core.Tests.Phone.Http;

public sealed class ResponseHeadTests
{
    const string Csp = "default-src 'none'";

    const string Common =
        "Connection: close\r\n" +
        "Content-Security-Policy: default-src 'none'\r\n" +
        "X-Content-Type-Options: nosniff\r\n" +
        "X-Frame-Options: DENY\r\n" +
        "Referrer-Policy: no-referrer\r\n" +
        "Cross-Origin-Opener-Policy: same-origin\r\n" +
        "Cross-Origin-Resource-Policy: same-origin\r\n";

    [Fact]
    public void An_empty_answer_is_exactly_this()
    {
        Assert.Equal(
            "HTTP/1.1 404 Not Found\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: 0\r\n" +
            Common + "Cache-Control: no-store\r\n\r\n",
            ResponseHead.Text(PhoneResponse.Empty(404), Csp));
    }

    [Fact]
    public void Its_own_headers_follow_in_order()
    {
        var response = PhoneResponse.Json(200, "{}"u8.ToArray()).With("ETag", "\"0123456789abcdef\"").With("X-A", "b");

        Assert.Equal(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\n" +
            Common + "Cache-Control: no-store\r\nETag: \"0123456789abcdef\"\r\nX-A: b\r\n\r\n",
            ResponseHead.Text(response, Csp));
    }

    [Theory]
    [InlineData("Cache-Control")]
    [InlineData("cache-control")]
    public void Its_own_cache_control_replaces_no_store(string name)
    {
        var response = PhoneResponse.Bytes(200, "image/jpeg", [1, 2, 3]).With(name, "private, max-age=31536000, immutable");

        Assert.Equal(
            "HTTP/1.1 200 OK\r\nContent-Type: image/jpeg\r\nContent-Length: 3\r\n" +
            Common + $"{name}: private, max-age=31536000, immutable\r\n\r\n",
            ResponseHead.Text(response, Csp));
    }

    [Fact]
    public void A_file_gives_its_length()
    {
        var head = ResponseHead.Text(PhoneResponse.File("image/png", @"C:\x.png", 5_000_000_000), Csp);

        Assert.StartsWith("HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: 5000000000\r\n", head);
    }

    [Theory]
    [InlineData(200, "OK")]
    [InlineData(421, "Misdirected Request")]
    [InlineData(418, "")]
    public void Each_status_has_its_reason(int status, string reason) =>
        Assert.Equal(reason, ResponseHead.Reason(status));

    [Fact]
    public void A_status_without_a_reason_keeps_its_space() =>
        Assert.StartsWith("HTTP/1.1 500 \r\n", ResponseHead.Text(PhoneResponse.Empty(500), Csp));

    [Fact]
    public void The_interim_answer_is_100_continue() =>
        Assert.Equal("HTTP/1.1 100 Continue\r\n\r\n", ResponseHead.Continue);
}
