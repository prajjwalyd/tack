using System.Text;
using Tack.Core.Phone.Http;

namespace Tack.Core.Tests.Phone.Http;

public sealed class HeadParserTests
{
    [Fact]
    public void Reads_a_head_and_what_came_after_it()
    {
        const string head = "POST /api/pin?v=1 HTTP/1.1\r\nHost: pc:7717\r\nContent-Length: 5\r\nX-Tack:  1 \r\n\r\n";
        var parse = Parse(head + "hel");

        Assert.Equal(Encoding.ASCII.GetByteCount(head), parse.Length);
        var read = parse.Head!;
        Assert.Equal(("POST", "/api/pin"), (read.Method, read.Path));
        Assert.Equal("1", read.Header("x-tack"));
        Assert.Equal("pc:7717", read.Header("HOST"));
        Assert.Equal(5, read.ContentLength);
        Assert.Equal("hel"u8.ToArray(), read.EarlyBody);
        Assert.Equal(["Host", "Content-Length", "X-Tack"], read.Headers.Select(h => h.Key));
    }

    [Fact]
    public void Without_a_length_there_is_no_body()
    {
        var head = Parse("GET / HTTP/1.0\r\n\r\n").Head!;

        Assert.Equal(0, head.ContentLength);
        Assert.Empty(head.EarlyBody);
        Assert.Empty(head.Headers);
        Assert.Null(head.Header("Content-Length"));
    }

    [Fact]
    public void Bare_line_feeds_and_blank_lines_before_the_request_are_taken()
    {
        const string head = "\r\n\nGET /phone.js HTTP/1.1\nHost: pc\n\n";
        var parse = Parse(head);

        Assert.Equal("/phone.js", parse.Head!.Path);
        Assert.Equal("pc", parse.Head.Header("Host"));
        Assert.Equal(head.Length, parse.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GET / HT")]
    [InlineData("GET / HTTP/1.1\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost: pc\r\n")]
    [InlineData("GET / HTTP/1.1\r\nHost: pc\r\n\r")]
    public void Not_yet_a_whole_head_is_partial(string bytes) => Assert.True(Parse(bytes).IsPartial);

    [Fact]
    public void A_bad_request_line_is_known_before_the_head_ends() =>
        Assert.Equal(HeadError.Malformed, Parse("NOT HTTP\r\nHost: pc").Error);

    [Fact]
    public void An_endless_head_is_refused()
    {
        var bytes = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nX-Long: " + new string('a', HttpLimits.MaxHead + 10));

        Assert.Equal(HeadError.HeadTooLarge, HeadParser.Parse(bytes).Error);
        Assert.Equal(HeadError.HeadTooLarge, HeadParser.Parse(bytes.AsSpan(0, HttpLimits.MaxHead)).Error);
        Assert.True(HeadParser.Parse(bytes.AsSpan(0, HttpLimits.MaxHead - 1)).IsPartial);
    }

    [Fact]
    public void A_head_of_exactly_the_limit_is_taken_and_one_byte_more_is_not()
    {
        var head = new StringBuilder("GET / HTTP/1.1\r\nX-Fill: ");
        head.Append('a', HttpLimits.MaxHead - head.Length - 4).Append("\r\n\r\n");
        Assert.Equal(HttpLimits.MaxHead, head.Length);

        Assert.Equal(HttpLimits.MaxHead, Parse(head.ToString()).Length);
        Assert.Equal(HeadError.HeadTooLarge, Parse(head.ToString().Replace("X-Fill: ", "X-Fill: a")).Error);
    }

    [Fact]
    public void Too_many_headers_are_refused()
    {
        Assert.Equal(HeadError.HeadTooLarge, Parse(WithHeaders(40)).Error);
        Assert.Equal(HeadError.HeadTooLarge, Parse(WithHeaders(HttpLimits.MaxHeaders + 1)).Error);
        Assert.Equal(HttpLimits.MaxHeaders, Parse(WithHeaders(HttpLimits.MaxHeaders)).Head!.Headers.Count);
    }

    [Theory]
    [InlineData("POST / HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n")]
    [InlineData("POST / HTTP/1.1\r\nContent-Length: 5\r\ntransfer-encoding: identity\r\n\r\n")]
    public void Any_transfer_encoding_is_refused(string head) =>
        Assert.Equal(HeadError.LengthRequired, Parse(head).Error);

    [Theory]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("1e3")]
    [InlineData("0x10")]
    [InlineData("5 5")]
    [InlineData("")]
    [InlineData("99999999999999999999")]
    [InlineData("٥")]
    public void A_bad_length_is_malformed(string length) =>
        Assert.Equal(HeadError.Malformed, Parse($"POST / HTTP/1.1\r\nContent-Length: {length}\r\n\r\n").Error);

    [Theory]
    [InlineData("7", 7)]
    [InlineData(" 0012 ", 12)]
    [InlineData("1000000000000", 1_000_000_000_000)]
    public void A_plain_length_is_read(string length, long expected) =>
        Assert.Equal(expected, Parse($"POST / HTTP/1.1\r\nContent-Length:{length}\r\n\r\n").Head!.ContentLength);

    [Theory]
    [InlineData("GET /\r\n\r\n")]
    [InlineData("GET  / HTTP/1.1\r\n\r\n")]
    [InlineData("GET / HTTP/2.0\r\n\r\n")]
    [InlineData("GET / HTTP/1.1 \r\n\r\n")]
    [InlineData("G(T / HTTP/1.1\r\n\r\n")]
    [InlineData("GET /a\tb HTTP/1.1\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nNo colon here\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\n: no name\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nName : space before the colon\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nA: b\r\n folded\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nA: b\u0001c\r\n\r\n")]
    [InlineData("GET / HTTP/1.1\r\nA: b\rc\r\n\r\n")]
    public void Not_http_is_malformed(string head) => Assert.Equal(HeadError.Malformed, Parse(head).Error);

    [Fact]
    public void A_value_that_is_not_utf8_is_malformed()
    {
        byte[] head = [.. "GET / HTTP/1.1\r\nA: "u8, 0xC3, 0x28, .. "\r\n\r\n"u8];

        Assert.Equal(HeadError.Malformed, HeadParser.Parse(head).Error);
    }

    [Fact]
    public void A_utf8_value_is_read_as_sent()
    {
        var head = Parse("GET / HTTP/1.1\r\nX-Name: café\tbar\r\n\r\n").Head!;

        Assert.Equal("café\tbar", head.Header("x-name"));
    }

    [Fact]
    public void Only_the_first_of_a_repeated_header_counts()
    {
        var head = Parse("POST / HTTP/1.1\r\nContent-Length: 3\r\nContent-Length: 9\r\n\r\n").Head!;

        Assert.Equal(3, head.ContentLength);
        Assert.Equal(2, head.Headers.Count);
    }

    [Theory]
    [InlineData(HeadError.Gone, null)]
    [InlineData(HeadError.HeadTooLarge, 431)]
    [InlineData(HeadError.Malformed, 400)]
    [InlineData(HeadError.LengthRequired, 411)]
    public void Each_error_has_its_status(HeadError error, int? status) => Assert.Equal(status, error.Status());

    static HeadParse Parse(string text) => HeadParser.Parse(Encoding.UTF8.GetBytes(text));

    static string WithHeaders(int count)
    {
        var head = new StringBuilder("GET / HTTP/1.1\r\n");
        for (var n = 0; n < count; n++)
        {
            head.Append($"X-{n}: 1\r\n");
        }
        return head.Append("\r\n").ToString();
    }
}
