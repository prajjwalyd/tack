using Tack.Core.Phone.Serving;
using static Tack.Core.Tests.Phone.Serving.PhoneBench;

namespace Tack.Core.Tests.Phone.Serving;

/// <summary>Linked files on the phone board: shown as cards, and a file (never a folder, never a path) downloads.</summary>
public sealed class FileRouteTests : IDisposable
{
    readonly Scratch _scratch = new("phone-files");
    readonly PhoneBench _bench = new();

    public void Dispose() => _scratch.Dispose();

    PhoneBench Allowed()
    {
        _bench.Host.Allowed.Add(PixelKey);
        return _bench;
    }

    string FileOf(string name, int length) => _scratch.File(name, new string('x', length));

    [Fact]
    public async Task A_file_streams_from_where_it_is_as_an_attachment()
    {
        var path = FileOf("Report Q3.docx", 1234);
        Allowed().Host.Board.Add(TestPrints.File("7", path, [1]));

        var response = await _bench.Get("/api/file/7");

        Assert.Equal(200, response.Status);
        Assert.Equal("application/octet-stream", response.ContentType);
        Assert.Equal((path, 1234L), (response.FilePath, response.Length));
        Assert.Empty(response.Body);
        Assert.Equal("""attachment; filename="Report Q3.docx"; filename*=UTF-8''Report%20Q3.docx""", response.Header("Content-Disposition"));
        // Not kept by the phone: the file may change on the PC.
        Assert.Null(response.Header("Cache-Control"));
        Assert.Empty(_bench.Host.Waited);
    }

    [Fact]
    public async Task A_file_needs_an_allowed_device()
    {
        _bench.Host.Board.Add(TestPrints.File("7", FileOf("a.zip", 3), [1]));

        var response = await _bench.Get("/api/file/7", Ours);

        Assert.Equal(403, response.Status);
        Assert.Null(response.FilePath);
        Assert.Empty(_bench.Host.Admitted);
    }

    [Theory]
    [InlineData("/api/file/8")]
    [InlineData("/api/file/")]
    [InlineData("/api/file/folder")]
    [InlineData("/api/file/gone")]
    [InlineData("/api/file/picture")]
    [InlineData("/api/file/note")]
    [InlineData("/api/file/..%5C..%5Cwindows%5Cwin.ini")]
    [InlineData("/api/file/C:%5Cwindows%5Cwin.ini")]
    public async Task Only_a_file_on_the_board_downloads(string path)
    {
        Allowed().Host.Board.AddRange(
        [
            TestPrints.File("7", FileOf("a.zip", 3), [1]),
            TestPrints.File("folder", _scratch.Folder("Projects"), [1], folder: true),
            TestPrints.File("gone", Path.Combine(_scratch.Path, "moved.pdf"), null),
            TestPrints.Picture("picture", FileOf("shot.png", 3), [1]),
            TestPrints.Note("note", "hello"),
        ]);

        var response = await _bench.Get(path);

        Assert.Equal(404, response.Status);
        Assert.Null(response.FilePath);
    }

    [Fact]
    public async Task A_files_thumbnail_is_served_but_never_as_a_full_picture()
    {
        Allowed().Host.Board.Add(TestPrints.File("7", FileOf("a.pdf", 3), [0xFF, 0xD8]));

        var thumb = await _bench.Get("/api/thumb/7");
        var full = await _bench.Get("/api/print/7");

        Assert.Equal((200, "image/jpeg"), (thumb.Status, thumb.ContentType));
        Assert.Equal([0xFF, 0xD8], thumb.Body);
        Assert.Equal(404, full.Status);
    }

    [Fact]
    public async Task The_board_shows_a_file_and_a_folder_as_cards()
    {
        Allowed().Host.Board.AddRange(
        [
            TestPrints.File("7", @"C:\Users\me\Report.docx", "foobar"u8.ToArray(), size: 2 * 1024 * 1024),
            TestPrints.File("8", @"C:\Users\me\Projects", null, folder: true, type: "File folder"),
        ]);

        var response = await _bench.Get("/api/board", Ours);

        const string File =
            """{"id":"7","note":false,"thumb":"/api/thumb/7?v=85944171f73967e8","full":"","width":240,"height":320,"text":null,"link":null,"pinnedAt":3000,"kept":false""" +
            ""","file":true,"name":"Report.docx","type":"Microsoft Word Document","size":"2 MB","download":"/api/file/7"}""";
        const string Folder =
            """{"id":"8","note":false,"thumb":"","full":"","width":0,"height":0,"text":null,"link":null,"pinnedAt":3000,"kept":false""" +
            ""","file":true,"name":"Projects","type":"Folder","size":"","download":""}""";
        Assert.Equal($$"""{"pc":"my-pc","you":"pixel","prints":[{{File}},{{Folder}}]}""", Text(response));
    }

    [Theory]
    [InlineData("plain.txt", """attachment; filename="plain.txt"; filename*=UTF-8''plain.txt""")]
    [InlineData("caf\u00e9 \"1\".pdf", """attachment; filename="caf_ _1_.pdf"; filename*=UTF-8''caf%C3%A9%20%221%22.pdf""")]
    [InlineData("a\\b;c%.zip", """attachment; filename="a_b;c%.zip"; filename*=UTF-8''a%5Cb%3Bc%25.zip""")]
    [InlineData("", """attachment; filename="file"; filename*=UTF-8''file""")]
    public void The_name_is_offered_plain_and_in_full(string name, string header) =>
        Assert.Equal(header, FileResponse.Disposition(name));
}
