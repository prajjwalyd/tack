using Tack.Core.Storage;

namespace Tack.Core.Tests.Storage;

public class MentionedPathsTests
{
    [Fact]
    public void Every_path_in_board_json_and_its_backups_is_mentioned()
    {
        using var scratch = new Scratch("mentioned");
        var file = Path.Combine(scratch.Path, "board.json");
        File.WriteAllText(file, """{ "version": 3, "prints": [ { "path": "C:\\Cap\\now.png" } ] }""");
        File.WriteAllText(file + ".bad-1", """{ "prints": [ { "path": "C:\\Cap\\kept \"1\".png" } ] }""");
        // Cut off mid-string: what is there still counts.
        File.WriteAllText(file + ".BAD-2", """{ "prints": [ { "path": "C:\\Cap\\cut""");
        // Not a backup of board.json.
        File.WriteAllText(Path.Combine(scratch.Path, "other.json"), """{ "path": "C:\\Cap\\other.png" }""");

        var mentioned = MentionedPaths.In(file)!;

        Assert.Contains(@"C:\Cap\now.png", mentioned);
        Assert.Contains(@"C:\Cap\kept ""1"".png", mentioned);
        // A string cut off at the end comes back raw, escapes and all; the sweep
        // compares full paths, where doubled separators make no difference.
        Assert.Contains(mentioned, p => Path.GetFullPath(p) == @"C:\Cap\cut");
        Assert.DoesNotContain(@"C:\Cap\other.png", mentioned);
    }

    [Fact]
    public void Nothing_is_mentioned_without_a_folder()
    {
        using var scratch = new Scratch("mentioned-none");

        Assert.Equal([], MentionedPaths.In(Path.Combine(scratch.Path, "nowhere", "board.json")));
    }
}
