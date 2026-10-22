using Tack.Core.NetBird;
using Tack.Core.Phone;
using Tack.Core.Phone.Serving;
using static Tack.Core.Tests.Phone.Serving.PhoneBench;

namespace Tack.Core.Tests.Phone.Serving;

public class PhoneRoutesGateTests
{
    public static TheoryData<Verdict, string> Refusals => new()
    {
        { new Verdict.Waiting(true, "0042"), """{"state":"waiting","code":"0042","pc":"my-pc","you":"pixel"}""" },
        { new Verdict.Waiting(false, "0042"), """{"state":"waiting","code":"0042","pc":"my-pc","you":"pixel"}""" },
        { Verdict.Busy.Instance, """{"state":"busy","pc":"my-pc","you":"pixel"}""" },
        { Verdict.Refused.Instance, """{"state":"denied","pc":"my-pc","you":"pixel"}""" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task The_board_tells_a_device_not_let_in_why(Verdict verdict, string json)
    {
        var bench = new PhoneBench();
        bench.Host.AdmitSays = verdict;

        var response = await bench.Get("/api/board", Ours);

        Assert.Equal(403, response.Status);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal(json, Text(response));
        Assert.Equal([PixelKey], bench.Host.Admitted);
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Asking_again_and_pinning_tell_it_the_same(Verdict verdict, string json)
    {
        var bench = new PhoneBench();
        (bench.Host.AdmitSays, bench.Host.AskAgainSays) = (verdict, verdict);

        var ask = await bench.Send(Request("POST", "/api/ask", Ours));
        var pin = await bench.Pin("text/plain", "hello"u8.ToArray());

        Assert.Equal((403, json), (ask.Status, Text(ask)));
        Assert.Equal((403, json), (pin.Status, Text(pin)));
        Assert.Empty(bench.Host.PinnedTexts);
    }

    [Fact]
    public async Task Without_x_tack_the_board_is_only_for_an_allowed_device()
    {
        var bench = new PhoneBench();
        bench.Host.AdmitSays = Verdict.Refused.Instance;

        var refused = await bench.Get("/api/board");
        bench.Host.Allowed.Add(PixelKey);
        var allowed = await bench.Get("/api/board");

        Assert.Equal(403, refused.Status);
        Assert.Equal(200, allowed.Status);
        Assert.Empty(bench.Host.Admitted);
    }

    [Fact]
    public async Task Pictures_never_ask_and_are_forbidden_unless_allowed()
    {
        var bench = new PhoneBench();
        bench.Host.Board.Add(TestPrints.Picture("1", "C:\\nowhere\\a.png", "abc"u8.ToArray()));

        var thumb = await bench.Get("/api/thumb/1", Ours);
        var full = await bench.Get("/api/print/1", Ours);

        Assert.Equal((403, 0), (thumb.Status, thumb.Body.Length));
        Assert.Equal((403, 0), (full.Status, full.Body.Length));
        Assert.Empty(bench.Host.Admitted);
        Assert.Empty(bench.Host.Waited);
    }

    [Fact]
    public async Task An_address_that_now_belongs_to_another_key_is_refused()
    {
        var other = PhoneBench.Me with { Peers = [Pixel with { Key = "b3RoZXI=" }] };
        var bench = new PhoneBench(booked: other);
        bench.Host.Allowed.Add(PixelKey);

        var board = await bench.Get("/api/board", Ours);
        var thumb = await bench.Get("/api/thumb/1");

        Assert.Equal("""{"state":"denied","pc":"my-pc","you":"pixel"}""", Text(board));
        Assert.Equal(403, thumb.Status);
        Assert.Empty(bench.Host.Admitted);
    }

    [Fact]
    public async Task An_address_netbird_no_longer_lists_is_refused()
    {
        var gone = PhoneBench.Me with { Peers = [] };
        var bench = new PhoneBench(booked: gone);
        bench.Host.Allowed.Add(PixelKey);

        var response = await bench.Get("/api/board", Ours);

        Assert.Equal(403, response.Status);
        Assert.Empty(bench.Host.Admitted);
    }

    [Fact]
    public async Task The_gate_looks_at_netbird_again_when_the_list_is_old()
    {
        var looks = 0;
        var moved = PhoneBench.Me with { Peers = [Pixel with { Key = "b3RoZXI=" }] };
        var time = new ManualTime();
        var peers = new PeerBook(PhoneBench.Me, () => { looks++; return Task.FromResult<NetBirdStatus?>(moved); }, time);
        var host = new FakeHost();
        var routes = new PhoneRoutes(host, PageFiles, peers, new(time));
        var from = new PeerMatch(PhoneBench.Me, Pixel);

        var before = await routes.HandleAsync(Request("GET", "/api/board", Ours), FakeBody.None, from);
        time.Advance(PeerBook.FreshFor);
        var after = await routes.HandleAsync(Request("GET", "/api/board", Ours), FakeBody.None, from);

        Assert.Equal(200, before.Status);
        Assert.Equal(403, after.Status);
        Assert.Equal(1, looks);
    }
}
