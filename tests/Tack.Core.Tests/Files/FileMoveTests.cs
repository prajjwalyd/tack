using Tack.Core.Files;

namespace Tack.Core.Tests.Files;

public class FileMoveTests
{
    [Fact]
    public void A_move_never_replaces_a_file_and_takes_the_next_free_name()
    {
        using var scratch = new Scratch("move");
        var from = scratch.File(@"Captures\Shot.png", "capture");
        var to = scratch.Folder("Screenshots");
        File.WriteAllText(Path.Combine(to, "Shot.png"), "the user's own");
        var tried = new List<string>();

        var moved = FileMove.ToFreeName(from, to, name =>
        {
            tried.Add(Path.GetFileName(name));
            // Another app takes the name between the look and the move.
            if (tried.Count == 1)
            {
                File.WriteAllText(name, "arrived meanwhile");
            }
            return true;
        });

        Assert.Equal(["Shot (2).png", "Shot (3).png"], tried);
        Assert.Equal(Path.Combine(to, "Shot (3).png"), moved);
        Assert.Equal("the user's own", File.ReadAllText(Path.Combine(to, "Shot.png")));
        Assert.Equal("arrived meanwhile", File.ReadAllText(Path.Combine(to, "Shot (2).png")));
        Assert.Equal("capture", File.ReadAllText(Path.Combine(to, "Shot (3).png")));
        Assert.False(File.Exists(from));
    }

    [Fact]
    public void A_called_off_move_leaves_the_file()
    {
        using var scratch = new Scratch("called-off");
        var from = scratch.File("a.png", "x");
        var target = scratch.Folder("out");

        var moved = FileMove.ToFreeName(from, target, _ => false);

        Assert.Null(moved);
        Assert.True(File.Exists(from));
    }

    [Fact]
    public void A_new_name_that_is_taken_is_refused_and_both_files_stay()
    {
        using var scratch = new Scratch("move-new");
        var from = scratch.File("a.png", "mine");
        var to = scratch.File("b.png", "theirs");

        Assert.Throws<FileExistsException>(() => FileMove.ToNewName(from, to));
        Assert.Equal("mine", File.ReadAllText(from));
        Assert.Equal("theirs", File.ReadAllText(to));
    }
}
