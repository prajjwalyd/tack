using Tack.Core.Prints;
using Tack.Core.Settings;
using Tack.Core.Storage;
using static Tack.Core.Tests.Storage.StorageFixture;

namespace Tack.Core.Tests.Storage;

/// <summary>
/// board.json's text escaping and its strictness about repeated fields. The
/// expected strings follow Tack 0.2's writer, which escaped only a quote, a
/// backslash and control characters, and refused a field named twice.
/// </summary>
public class BoardJsonTests
{
    [Theory]
    // An emoji, outside the Basic Multilingual Plane, is written as it is.
    [InlineData("C:\\Users\\me\\Pictures\\\U0001F642 smile.png", "C:\\\\Users\\\\me\\\\Pictures\\\\\U0001F642 smile.png")]
    // So are the line and paragraph separators.
    [InlineData("line\u2028para\u2029end", "line\u2028para\u2029end")]
    // Control characters: five by name, the rest in lower-case hex. DEL and C1 controls are not escaped.
    [InlineData("\0\u0001\b\t\n\u000b\f\r\u001b\u001f \u007f\u0080\u00a0", "\\u0000\\u0001\\b\\t\\n\\u000b\\f\\r\\u001b\\u001f \u007f\u0080\u00a0")]
    // Quotes and backslashes are escaped; HTML-sensitive characters are not.
    [InlineData("say \"hi\" \\ back/slash <tag> & 'q' +", "say \\\"hi\\\" \\\\ back/slash <tag> & 'q' +")]
    [InlineData("caf\u00e9 \u65e5\u672c \ufffd \ufeff", "caf\u00e9 \u65e5\u672c \ufffd \ufeff")]
    public void Text_is_escaped_as_tack_0_2_escaped_it(string path, string written)
    {
        using var scratch = new Scratch("escaping");
        var file = Path.Combine(scratch.Path, "board.json");

        BoardFile.Write(file, Snapshot.Of([], [new SavedPrint(path, Kind.Image, Origin.Folder, Now, null)], new TackSettings()));
        var text = File.ReadAllText(file);
        var (saved, loaded) = BoardFile.Load(file, Now);

        Assert.Contains($"\n      \"path\": \"{written}\",\n", text);
        Assert.Equal(Loaded.Ok.Instance, loaded);
        Assert.Equal(path, saved.Prints[0].Path);
    }

    [Theory]
    [InlineData("""{ "version": 3, "version": 3, "prints": [] }""")]
    [InlineData("""{ "version": 3, "prints": [], "prints": [] }""")]
    [InlineData("""{ "prints": [], "sound": true, "sound": false }""")]
    [InlineData("""{ "prints": [], "reveal": "normal", "reveal": "fast" }""")]
    [InlineData("""{ "prints": [], "phone": { "on": true, "on": true } }""")]
    [InlineData("""{ "prints": [ { "path": "C:\\a.png", "path": "C:\\b.png" } ] }""")]
    public void A_field_named_twice_fails_the_file(string json)
    {
        var (saved, loaded) = Parse(json);

        Assert.Empty(saved.Prints);
        Assert.IsType<Loaded.Unreadable>(loaded);
    }

    [Fact]
    public void An_unknown_field_named_twice_is_still_ignored() =>
        Assert.Equal(Loaded.Ok.Instance, Parse("""{ "prints": [ { "path": "C:\\a.png", "x": 1, "x": 2 } ], "later": 1, "later": 2 }""").Loaded);

    [Fact]
    public void A_board_json_with_a_field_named_twice_is_backed_up()
    {
        using var scratch = new Scratch("duplicate");
        var file = Path.Combine(scratch.Path, "board.json");
        const string Contents = """{ "version": 3, "prints": [], "version": 4 }""";
        File.WriteAllText(file, Contents);

        var (_, loaded) = BoardFile.Load(file, Now);

        var backup = Assert.IsType<Loaded.Unreadable>(loaded).Backup;
        Assert.Equal($"{file}.bad-{Now}", backup);
        Assert.Equal(Contents, File.ReadAllText(backup!));
    }
}
