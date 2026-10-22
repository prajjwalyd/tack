using Tack.Core.Phone.Serving;
using static Tack.Core.Tests.Phone.Serving.PhoneBench;

namespace Tack.Core.Tests.Phone.Serving;

public class PinRouteTests
{
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0];

    static void Said(int status, string words, Tack.Core.Phone.Http.PhoneResponse response)
    {
        Assert.Equal((status, words), (response.Status, Text(response)));
        Assert.Equal("text/plain; charset=utf-8", response.ContentType);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("IMAGE/PNG; name=x")]
    [InlineData("  image/jpeg ;q=1")]
    public async Task A_picture_is_pinned_as_a_photo_from_the_device(string type)
    {
        var bench = new PhoneBench();

        var response = await bench.Pin(type, Jpeg);

        Said(200, "Pinned", response);
        Assert.Equal([("photo from pixel", Jpeg)], bench.Host.PinnedImages);
        Assert.Equal([PixelKey], bench.Host.Admitted);
    }

    [Fact]
    public async Task A_picture_that_cannot_be_pinned_says_so()
    {
        var bench = new PhoneBench();
        bench.Host.ImagePins = false;

        Said(422, "Couldn't pin that picture", await bench.Pin("image/png", Jpeg));
    }

    [Fact]
    public async Task Text_is_pinned_as_it_was_sent()
    {
        var bench = new PhoneBench();

        var response = await bench.Pin("text/plain; charset=utf-8", "\n\n  hello\r\nthere  \n"u8.ToArray());

        Said(200, "Pinned", response);
        Assert.Equal(["\n\n  hello\r\nthere  \n"], bench.Host.PinnedTexts);
        Assert.Empty(bench.Host.PinnedImages);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("image/gif")]
    [InlineData("text/html")]
    [InlineData("")]
    public async Task Only_photos_and_text_come_first(string type)
    {
        var bench = new PhoneBench();
        var body = new FakeBody(Jpeg);

        var response = await bench.Send(
            Request("POST", "/api/pin", Ours, Header("Content-Type", type), Header("Content-Length", "999999999")), body);

        Said(415, "Only photos and text can be pinned", response);
        Assert.Null(body.AskedMax);
    }

    [Fact]
    public async Task A_missing_type_is_refused_as_any_other()
    {
        var response = await new PhoneBench().Send(Request("POST", "/api/pin", Ours));

        Said(415, "Only photos and text can be pinned", response);
    }

    [Fact]
    public async Task Too_large_comes_before_the_limits_and_counts_no_pin()
    {
        var bench = new PhoneBench();
        var body = new FakeBody(Jpeg);
        var length = $"{PhoneRoutes.MaxPin + 1}";

        var response = await bench.Send(
            Request("POST", "/api/pin", Ours, Header("Content-Type", "image/png"), Header("Content-Length", length)), body);

        Said(413, "That's too large to pin", response);
        Assert.Null(body.AskedMax);
        for (var i = 0; i < PinLimits.PinsPerMinute; i++)
        {
            Assert.True(bench.Limits.MayPin(PixelKey));
        }
    }

    [Fact]
    public async Task Twenty_mib_exactly_may_be_pinned()
    {
        var bench = new PhoneBench();
        var body = new FakeBody(Jpeg);

        await bench.Send(Request("POST", "/api/pin", Ours, Header("Content-Type", "image/png"),
            Header("Content-Length", $"{PhoneRoutes.MaxPin}")), body);

        Assert.Equal(PhoneRoutes.MaxPin, body.AskedMax);
    }

    [Fact]
    public async Task The_thirty_first_pin_in_a_minute_is_slowed_down()
    {
        var bench = new PhoneBench();
        for (var i = 0; i < PinLimits.PinsPerMinute; i++)
        {
            Said(200, "Pinned", await bench.Pin("image/jpeg", Jpeg));
        }

        var body = new FakeBody(Jpeg);
        var over = await bench.Pin("image/jpeg", Jpeg, body);
        bench.Time.Advance(TimeSpan.FromSeconds(60));
        var later = await bench.Pin("image/jpeg", Jpeg);

        Said(429, "Slow down a little", over);
        Assert.Null(body.AskedMax);
        Said(200, "Pinned", later);
    }

    [Fact]
    public async Task With_two_pins_under_way_a_third_is_told_busy()
    {
        var bench = new PhoneBench();
        using var one = bench.Limits.TryTurn();
        using var two = bench.Limits.TryTurn();
        var body = new FakeBody(Jpeg);

        var response = await bench.Pin("image/jpeg", Jpeg, body);

        Said(503, "Busy, try again in a moment", response);
        Assert.Null(body.AskedMax);
    }

    [Fact]
    public async Task The_turn_is_held_until_the_pin_is_done()
    {
        var bench = new PhoneBench();
        var finish = new TaskCompletionSource();
        bench.Host.ImagePinWaitsFor = finish.Task;

        var pinning = bench.Pin("image/jpeg", Jpeg);
        var second = bench.Limits.TryTurn();
        var third = bench.Limits.TryTurn();
        finish.SetResult();
        Said(200, "Pinned", await pinning);

        Assert.NotNull(second);
        Assert.Null(third);
        second.Dispose();
        using var again = bench.Limits.TryTurn();
        using var andAgain = bench.Limits.TryTurn();
        Assert.NotNull(again);
        Assert.NotNull(andAgain);
    }

    [Fact]
    public async Task A_body_that_does_not_arrive_says_so_and_gives_the_turn_back()
    {
        var bench = new PhoneBench();
        var body = new FakeBody(null);

        var response = await bench.Pin("image/jpeg", Jpeg, body);

        Said(413, "That didn't arrive in full", response);
        Assert.Equal(PhoneRoutes.MaxPin, body.AskedMax);
        using var one = bench.Limits.TryTurn();
        using var two = bench.Limits.TryTurn();
        Assert.NotNull(two);
    }

    [Fact]
    public async Task Text_must_be_utf_8()
    {
        var bench = new PhoneBench();

        Said(415, "Text must be UTF-8", await bench.Pin("text/plain", [0x68, 0xFF, 0x69]));
        Said(415, "Text must be UTF-8", await bench.Pin("text/plain", [0xED, 0xA0, 0x80]));
        Assert.Empty(bench.Host.PinnedTexts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t\r\n ")]
    public async Task Text_with_nothing_in_it_is_not_pinned(string text)
    {
        var bench = new PhoneBench();

        Said(422, "Nothing to pin", await bench.Pin("text/plain", System.Text.Encoding.UTF8.GetBytes(text)));
        Assert.Empty(bench.Host.PinnedTexts);
    }

    [Fact]
    public async Task Text_already_on_the_board_is_not_pinned_again()
    {
        var bench = new PhoneBench();
        bench.Host.Board.Add(TestPrints.Note("1", "hello"));

        // The note's text, not the text as sent, is what counts.
        Said(409, "Already on your board", await bench.Pin("text/plain", "\r\nhello  \r\n"u8.ToArray()));
        Assert.Empty(bench.Host.PinnedTexts);
    }

    [Fact]
    public async Task Each_device_has_a_minute_of_its_own()
    {
        var bench = new PhoneBench();
        for (var i = 0; i < PinLimits.PinsPerMinute; i++)
        {
            bench.Limits.MayPin("b3RoZXI=");
        }

        Said(200, "Pinned", await bench.Pin("image/jpeg", Jpeg));
    }
}
