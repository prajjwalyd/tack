using Tack.Core.Phone.Serving;
using static Tack.Core.Tests.Phone.Serving.PhoneBench;

namespace Tack.Core.Tests.Phone.Serving;

public sealed class PictureRouteTests : IDisposable
{
    const string Keep = "private, max-age=31536000, immutable";

    readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "tack-tests", "phone-" + Guid.NewGuid().ToString("N"))).FullName;

    readonly PhoneBench _bench = new();

    public PictureRouteTests() => _bench.Host.Allowed.Add(PixelKey);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string FileOf(string name, long length)
    {
        var path = Path.Combine(_dir, name);
        using var file = File.Create(path);
        file.SetLength(length);
        return path;
    }

    [Fact]
    public async Task A_thumbnail_is_its_jpeg_and_may_be_kept()
    {
        _bench.Host.Board.Add(TestPrints.Picture("1", FileOf("a.png", 10), [0xFF, 0xD8, 0xFF]));

        var response = await _bench.Get("/api/thumb/1");

        Assert.Equal(200, response.Status);
        Assert.Equal("image/jpeg", response.ContentType);
        Assert.Equal([0xFF, 0xD8, 0xFF], response.Body);
        Assert.Equal(Keep, response.Header("Cache-Control"));
    }

    [Theory]
    [InlineData("/api/thumb/2")]
    [InlineData("/api/thumb/")]
    [InlineData("/api/thumb/n")]
    [InlineData("/api/print/2")]
    [InlineData("/api/print/n")]
    public async Task Anything_but_a_picture_on_the_board_is_not_found(string path)
    {
        _bench.Host.Board.Add(TestPrints.Note("n", "hello"));

        var response = await _bench.Get(path);

        Assert.Equal(404, response.Status);
        Assert.Empty(response.Body);
        Assert.Empty(_bench.Host.Waited);
    }

    [Theory]
    [InlineData("shot.png", "image/png")]
    [InlineData("shot.PNG", "image/png")]
    [InlineData("shot.Png", "image/png")]
    [InlineData("shot.jpg", "image/jpeg")]
    [InlineData("shot.jpeg", "image/jpeg")]
    [InlineData("shot.webp", "image/jpeg")]
    [InlineData("shot", "image/jpeg")]
    public async Task A_picture_is_streamed_from_its_file_once_saved(string name, string type)
    {
        var path = FileOf(name, 1234);
        _bench.Host.Board.Add(TestPrints.Picture("1", path, [1]));

        var response = await _bench.Get("/api/print/1");

        Assert.Equal(200, response.Status);
        Assert.Equal(type, response.ContentType);
        Assert.Equal(path, response.FilePath);
        Assert.Equal(1234, response.Length);
        Assert.Empty(response.Body);
        Assert.Equal(Keep, response.Header("Cache-Control"));
        Assert.Equal([path], _bench.Host.Waited);
    }

    [Fact]
    public async Task A_picture_up_to_64_mib_is_served()
    {
        _bench.Host.Board.Add(TestPrints.Picture("1", FileOf("big.png", PhoneRoutes.MaxServed), [1]));

        var response = await _bench.Get("/api/print/1");

        Assert.Equal(200, response.Status);
        Assert.Equal(64L * 1024 * 1024, response.Length);
    }

    [Fact]
    public async Task A_picture_over_64_mib_is_not_found()
    {
        _bench.Host.Board.Add(TestPrints.Picture("1", FileOf("huge.png", PhoneRoutes.MaxServed + 1), [1]));

        var response = await _bench.Get("/api/print/1");

        Assert.Equal(404, response.Status);
        Assert.Null(response.FilePath);
    }

    [Fact]
    public async Task An_empty_or_missing_file_is_not_found_after_waiting_for_it()
    {
        var missing = Path.Combine(_dir, "gone.png");
        _bench.Host.Board.Add(TestPrints.Picture("1", FileOf("empty.png", 0), [1]));
        _bench.Host.Board.Add(TestPrints.Picture("2", missing, [1]));
        _bench.Host.Board.Add(TestPrints.Picture("3", _dir, [1]));

        var empty = await _bench.Get("/api/print/1");
        var gone = await _bench.Get("/api/print/2");
        var folder = await _bench.Get("/api/print/3");

        Assert.All([empty, gone, folder], r => Assert.Equal(404, r.Status));
        Assert.Equal([Path.Combine(_dir, "empty.png"), missing, _dir], _bench.Host.Waited);
    }
}
