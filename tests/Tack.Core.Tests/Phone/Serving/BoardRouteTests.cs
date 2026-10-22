using Tack.Core.Phone.Serving;
using static Tack.Core.Tests.Phone.Serving.PhoneBench;

namespace Tack.Core.Tests.Phone.Serving;

public class BoardRouteTests
{
    /// <summary>FNV-1a 64 of "foobar", from the published test vectors.</summary>
    const string FoobarV = "85944171f73967e8";

    static PhoneBench Allowed()
    {
        var bench = new PhoneBench();
        bench.Host.Allowed.Add(PixelKey);
        return bench;
    }

    [Theory]
    [InlineData("", "cbf29ce484222325")]
    [InlineData("a", "af63dc4c8601ec8c")]
    [InlineData("foobar", FoobarV)]
    public void Digests_are_64_bit_fnv_1a_in_16_hex_digits(string text, string hex) =>
        Assert.Equal(hex, Fnv1a.Hex(System.Text.Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task The_board_names_every_field_as_the_page_reads_it()
    {
        var bench = Allowed();
        bench.Host.Board.AddRange(
        [
            TestPrints.Picture("1", "C:\\shots\\a.png", "foobar"u8.ToArray(), 800, 600, pinnedAt: 1_000, keptAt: 5),
            TestPrints.Note("2", "caf\u00e9 \"quoted\"", pinnedAt: 2_000),
            TestPrints.Note("3", "https://example.com/?a=1&b=2", pinnedAt: 3_000),
        ]);

        var response = await bench.Get("/api/board", Ours);

        const string Picture =
            $$"""{"id":"1","note":false,"thumb":"/api/thumb/1?v={{FoobarV}}","full":"/api/print/1?v={{FoobarV}}","width":800,"height":600,"text":null,"link":null,"pinnedAt":1000,"kept":true}""";
        var note =
            """{"id":"2","note":true,"thumb":"","full":"","width":0,"height":0,"text":""" +
            "\"caf\u00e9 \\\"quoted\\\"\"" +
            ""","link":null,"pinnedAt":2000,"kept":false}""";
        const string Link =
            """{"id":"3","note":true,"thumb":"","full":"","width":0,"height":0,"text":"https://example.com/?a=1&b=2","link":"https://example.com/?a=1&b=2","pinnedAt":3000,"kept":false}""";
        Assert.Equal(200, response.Status);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal($$"""{"pc":"my-pc","you":"pixel","prints":[{{Picture}},{{note}},{{Link}}]}""", Text(response));
    }

    [Fact]
    public async Task An_empty_board_is_an_empty_list()
    {
        var response = await Allowed().Get("/api/board", Ours);

        Assert.Equal("""{"pc":"my-pc","you":"pixel","prints":[]}""", Text(response));
    }

    [Fact]
    public async Task A_picture_gets_a_new_address_when_its_thumbnail_changes()
    {
        var bench = Allowed();
        bench.Host.Board.Add(TestPrints.Picture("7", "C:\\shots\\a.png", "a"u8.ToArray()));

        var text = Text(await bench.Get("/api/board", Ours));

        Assert.Contains("\"thumb\":\"/api/thumb/7?v=af63dc4c8601ec8c\"", text);
        Assert.Contains("\"full\":\"/api/print/7?v=af63dc4c8601ec8c\"", text);
    }

    [Fact]
    public async Task The_etag_is_the_quoted_digest_of_the_body()
    {
        var bench = Allowed();
        bench.Host.Board.Add(TestPrints.Note("1", "hello"));

        var response = await bench.Get("/api/board", Ours);

        Assert.Equal($"\"{Fnv1a.Hex(response.Body)}\"", response.Header("ETag"));
        Assert.Equal(18, response.Header("ETag")!.Length);
    }

    [Fact]
    public async Task A_page_showing_this_board_is_told_not_modified()
    {
        var bench = Allowed();
        bench.Host.Board.Add(TestPrints.Note("1", "hello"));
        var tag = (await bench.Get("/api/board", Ours)).Header("ETag")!;

        var same = await bench.Get("/api/board", Ours, Header("If-None-Match", tag));

        Assert.Equal(304, same.Status);
        Assert.Empty(same.Body);
        Assert.Equal(tag, same.Header("ETag"));
    }

    [Fact]
    public async Task A_changed_board_or_another_tag_gets_the_board()
    {
        var bench = Allowed();
        bench.Host.Board.Add(TestPrints.Note("1", "hello"));
        var tag = (await bench.Get("/api/board", Ours)).Header("ETag")!;

        var unquoted = await bench.Get("/api/board", Ours, Header("If-None-Match", tag.Trim('"')));
        var star = await bench.Get("/api/board", Ours, Header("If-None-Match", "*"));
        bench.Host.Board.Add(TestPrints.Note("2", "world"));
        var changed = await bench.Get("/api/board", Ours, Header("If-None-Match", tag));

        Assert.Equal(200, unquoted.Status);
        Assert.Equal(200, star.Status);
        Assert.Equal(200, changed.Status);
        Assert.NotEqual(tag, changed.Header("ETag"));
        Assert.Contains("world", Text(changed));
    }

    [Fact]
    public async Task The_board_is_not_tagged_for_a_device_not_let_in()
    {
        var response = await new PhoneBench().Get("/api/board");

        Assert.Equal(403, response.Status);
        Assert.Null(response.Header("ETag"));
    }
}
