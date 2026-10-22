using Tack.Core.Captures;
using Tack.Core.Settings;
using Tack.Core.Storage;
using Tack.Core.Tests.Files;

namespace Tack.Core.Tests.Storage;

/// <summary>The startup rule end to end: board.json decides whether, and what, the sweep may take.</summary>
public class BoardSweepTests
{
    static DateTime NineDaysOn() => Sweep.Cutoff(DateTime.UtcNow.AddDays(9), boardSaved: null);

    [Theory]
    [InlineData("missing", null)]
    [InlineData("corrupt", """{ "prints": [""")]
    [InlineData("newer", """{ "version": 7, "prints": [] }""")]
    [InlineData("no prints", """{ "version": 3, "sound": false }""")]
    [InlineData("understood", """{ "version": 3, "prints": [] }""")]
    public void Only_an_understood_board_json_lets_the_sweep_run(string name, string? contents)
    {
        using var scratch = new Scratch("board-sweep");
        var captures = scratch.Folder("Captures");
        var leftover = scratch.File(@"Captures\Screenshot 2026-10-08 023223.png", "x");
        var file = Path.Combine(scratch.Path, "board.json");
        if (contents != null)
        {
            File.WriteAllText(file, contents);
        }

        var (saved, loaded) = BoardFile.Load(file, StorageFixture.Now);
        var bin = new NotingBin();
        if (loaded is Loaded.Ok)
        {
            Sweep.Captures(captures, [.. saved.Prints.Select(p => p.Path)], NineDaysOn(), bin);
        }

        Assert.Equal(name == "understood" ? [leftover] : [], bin.Taken);
    }

    [Fact]
    public void A_path_in_any_backup_is_never_swept()
    {
        using var scratch = new Scratch("backup-sweep");
        var captures = scratch.Folder("Captures");
        var kept = scratch.File(@"Captures\Screenshot 2026-01-01 101010.png", "x");
        // Kept in a board.json that was backed up, then replaced by one without it.
        var file = Path.Combine(scratch.Path, "board.json");
        File.WriteAllText(file + ".bad-5", $$"""{ "prints": [ { "path": {{System.Text.Json.JsonSerializer.Serialize(kept)}} } ] }""");
        BoardFile.Write(file, Snapshot.Of([], [], new TackSettings()));

        var (saved, loaded) = BoardFile.Load(file, StorageFixture.Now);
        List<string> keep = [.. saved.Prints.Select(p => p.Path), .. MentionedPaths.In(file)!];
        var bin = new NotingBin();
        Sweep.Captures(captures, keep, NineDaysOn(), bin);

        Assert.Equal(Loaded.Ok.Instance, loaded);
        Assert.Empty(bin.Taken);
    }
}
