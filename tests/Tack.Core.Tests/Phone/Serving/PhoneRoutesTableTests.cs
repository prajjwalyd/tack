using Tack.Core.Phone;
using static Tack.Core.Tests.Phone.Serving.PhoneBench;

namespace Tack.Core.Tests.Phone.Serving;

public class PhoneRoutesTableTests
{
    const string Manifest =
        """{"name":"Tack","short_name":"Tack","start_url":"/","display":"standalone","background_color":"#153328","theme_color":"#1c4234","icons":[{"src":"/icon.png","sizes":"256x256","type":"image/png"}]}""";

    [Theory]
    [InlineData("/", "text/html; charset=utf-8", "page")]
    [InlineData("/phone.js", "text/javascript; charset=utf-8", "script")]
    [InlineData("/phone.css", "text/css; charset=utf-8", "style")]
    [InlineData("/manifest.webmanifest", "application/manifest+json", Manifest)]
    public async Task The_page_and_its_files_go_to_any_peer(string path, string type, string body)
    {
        var bench = new PhoneBench();

        var response = await bench.Get(path);

        Assert.Equal(200, response.Status);
        Assert.Equal(type, response.ContentType);
        Assert.Equal(body, Text(response));
        Assert.Null(response.Header("Cache-Control"));
    }

    [Fact]
    public async Task The_icon_is_a_png()
    {
        var response = await new PhoneBench().Get("/icon.png");

        Assert.Equal(200, response.Status);
        Assert.Equal("image/png", response.ContentType);
        Assert.Equal(PageFiles.Icon, response.Body);
    }

    [Fact]
    public async Task Asking_again_from_the_page_says_allowed_or_why_not()
    {
        var bench = new PhoneBench();

        var allowed = await bench.Send(Request("POST", "/api/ask", Ours));
        bench.Host.AskAgainSays = Verdict.Refused.Instance;
        var refused = await bench.Send(Request("POST", "/api/ask", Ours));

        Assert.Equal(200, allowed.Status);
        Assert.Equal("application/json", allowed.ContentType);
        Assert.Equal("""{"state":"allowed"}""", Text(allowed));
        Assert.Equal(403, refused.Status);
        Assert.Equal("""{"state":"denied","pc":"my-pc","you":"pixel"}""", Text(refused));
    }

    [Theory]
    [InlineData("/api/ask")]
    [InlineData("/api/pin")]
    [InlineData("/api/board")]
    [InlineData("/api/anything")]
    public async Task Any_other_post_under_api_is_forbidden(string path)
    {
        var bench = new PhoneBench();

        var response = await bench.Send(Request("POST", path));

        Assert.Equal(403, response.Status);
        Assert.Empty(response.Body);
        Assert.Empty(bench.Host.Admitted);
    }

    [Theory]
    [InlineData("GET", "/nothing")]
    [InlineData("GET", "/api/nothing")]
    [InlineData("GET", "/api")]
    [InlineData("POST", "/")]
    [InlineData("POST", "/nothing")]
    public async Task Any_other_get_or_post_is_not_found(string method, string path)
    {
        var response = await new PhoneBench().Send(Request(method, path, Ours));

        Assert.Equal(404, response.Status);
        Assert.Empty(response.Body);
    }

    [Theory]
    [InlineData("PUT", "/")]
    [InlineData("DELETE", "/api/board")]
    [InlineData("OPTIONS", "/api/pin")]
    [InlineData("get", "/")]
    public async Task Any_other_method_is_not_allowed(string method, string path)
    {
        var response = await new PhoneBench().Send(Request(method, path, Ours));

        Assert.Equal(405, response.Status);
        Assert.Empty(response.Body);
    }

    [Theory]
    [InlineData("my-pc.netbird.cloud:7717")]
    [InlineData("MY-PC.NetBird.Cloud:7717")]
    [InlineData("100.90.0.1:7717")]
    public async Task Host_must_name_this_pc_on_the_port(string host)
    {
        var response = await new PhoneBench().Get("/", Header("Host", host));

        Assert.Equal(200, response.Status);
    }

    [Theory]
    [InlineData("evil.example:7717")]
    [InlineData("my-pc.netbird.cloud")]
    [InlineData("my-pc.netbird.cloud:80")]
    [InlineData("100.90.0.1")]
    [InlineData("100.90.0.2:7717")]
    [InlineData("my-pc:7717")]
    [InlineData("")]
    public async Task Any_other_host_is_misdirected_before_anything_else(string host)
    {
        var bench = new PhoneBench();

        var page = await bench.Get("/", Header("Host", host));
        var pin = await bench.Send(Request("POST", "/api/pin", Header("Host", host), Ours));
        var odd = await bench.Send(Request("PUT", "/", Header("Host", host)));

        Assert.All([page, pin, odd], r => Assert.Equal(421, r.Status));
        Assert.Empty(page.Body);
        Assert.Empty(bench.Host.Admitted);
    }

    [Fact]
    public async Task A_request_without_host_is_misdirected()
    {
        var head = new Tack.Core.Phone.Http.RequestHead("GET", "/", [], 0, []);

        var response = await new PhoneBench().Send(head);

        Assert.Equal(421, response.Status);
    }

    [Theory]
    [InlineData(MyHost, "http://my-pc.netbird.cloud:7717")]
    [InlineData(MyHost, "HTTP://My-PC.netbird.cloud:7717")]
    [InlineData("MY-PC.netbird.cloud:7717", "http://my-pc.NETBIRD.cloud:7717")]
    [InlineData("100.90.0.1:7717", "http://100.90.0.1:7717")]
    public async Task An_origin_from_this_page_is_let_through(string host, string origin)
    {
        var response = await new PhoneBench().Get("/", Header("Host", host), Header("Origin", origin));

        Assert.Equal(200, response.Status);
    }

    [Theory]
    [InlineData(MyHost, "http://evil.example")]
    [InlineData(MyHost, "https://my-pc.netbird.cloud:7717")]
    [InlineData(MyHost, "http://my-pc.netbird.cloud")]
    [InlineData(MyHost, "http://100.90.0.1:7717")]
    [InlineData("100.90.0.1:7717", "http://my-pc.netbird.cloud:7717")]
    [InlineData(MyHost, "null")]
    [InlineData(MyHost, "")]
    public async Task An_origin_from_anywhere_else_is_forbidden(string host, string origin)
    {
        var bench = new PhoneBench();

        var page = await bench.Get("/", Header("Host", host), Header("Origin", origin));
        var board = await bench.Get("/api/board", Header("Host", host), Header("Origin", origin), Ours);

        Assert.Equal(403, page.Status);
        Assert.Empty(page.Body);
        Assert.Equal(403, board.Status);
        Assert.Empty(board.Body);
        Assert.Empty(bench.Host.Admitted);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("11")]
    public async Task Only_x_tack_exactly_1_is_the_page(string value)
    {
        var bench = new PhoneBench();
        bench.Host.AdmitSays = new Verdict.Waiting(true, "0042");

        var ask = await bench.Send(Request("POST", "/api/ask", Header("X-Tack", value)));
        var board = await bench.Get("/api/board", Header("X-Tack", value));

        Assert.Equal(403, ask.Status);
        Assert.Empty(ask.Body);
        // Not the page: served only to a device already allowed, and never asks.
        Assert.Equal("""{"state":"denied","pc":"my-pc","you":"pixel"}""", Text(board));
        Assert.Empty(bench.Host.Admitted);
    }
}
