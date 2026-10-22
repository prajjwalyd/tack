using Tack.Core.Appearance;
using Tack.Core.Phone;
using Tack.Core.Prints;
using Tack.Core.Reveal;
using Tack.Core.Settings;
using Tack.Core.Storage;
using static Tack.Core.Tests.Storage.StorageFixture;

namespace Tack.Core.Tests.Storage;

public class BoardFileSaveTests
{
    [Fact]
    public void Board_json_is_written_byte_for_byte_as_tack_0_2_wrote_it()
    {
        using var scratch = new Scratch("format");
        var file = Path.Combine(scratch.Path, "board.json");
        var expected = Fixture("board-v0.2.json");

        var (saved, _) = BoardFile.Parse(System.Text.Encoding.UTF8.GetBytes(expected), Now);
        BoardFile.Write(file, Snapshot.Of([], saved.Prints, saved.Settings));

        Assert.Equal(expected, File.ReadAllText(file));
    }

    [Fact]
    public void A_real_board_json_round_trips_unchanged()
    {
        // Set TACK_REAL_BOARD to a board.json written by Tack 0.2 to check it here;
        // it is read, never written.
        var real = Environment.GetEnvironmentVariable("TACK_REAL_BOARD");
        Assert.SkipWhen(string.IsNullOrEmpty(real) || !File.Exists(real), "TACK_REAL_BOARD is not set.");
        using var scratch = new Scratch("real");
        var copy = Path.Combine(scratch.Path, "board.json");
        var original = File.ReadAllBytes(real!);

        var (saved, loaded) = BoardFile.Parse(original, Now);
        BoardFile.Write(copy, Snapshot.Of([], saved.Prints, saved.Settings));

        Assert.Equal(Loaded.Ok.Instance, loaded);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(original), File.ReadAllText(copy));
    }

    [Fact]
    public void Only_notes_are_saved_with_a_kind()
    {
        using var scratch = new Scratch("kinds");
        var file = Path.Combine(scratch.Path, "board.json");

        Save(file, [Print(scratch.Path, "1", "a.png", Origin.Folder, Now - 1, null), Print(scratch.Path, "2", "Note.txt", Origin.Capture, Now - 2, null)], new TackSettings());

        var text = File.ReadAllText(file);
        Assert.Single(text.Split("\"kind\"")[1..]);
        Assert.Contains("\"kind\": \"note\"", text);
        Assert.DoesNotContain("the words of", text);
        Assert.Contains("\"toggleShortcut\": \"Win+Alt+S\"", text);
    }

    [Fact]
    public void Save_then_load_round_trips_without_leaving_a_temp_file()
    {
        using var scratch = new Scratch("round-trip");
        var file = Path.Combine(scratch.Path, "board.json");
        Print[] prints =
        [
            Print(scratch.Path, "1", "a.png", Origin.Folder, Now - 50, Now - 10),
            Print(scratch.Path, "2", "b.png", Origin.Capture, Now - 20, null),
            Print(scratch.Path, "3", "c.txt", Origin.Capture, Now - 30, null),
        ];
        var settings = new TackSettings
        {
            Sound = false,
            EdgeReveal = false,
            Reveal = RevealSpeed.Normal,
            Appearance = new AppearanceSettings { Felt = Felt.Parse("#a1b2c3"), Frame = Frame.Parse("graphite"), Paper = NotePaper.Parse("sky") },
            Updates = new UpdateSettings { Check = false },
            SnipTipShown = true,
            ToggleShortcut = "Ctrl+Alt+F9",
            PinShortcut = "",
            Phone = new PhoneSettings { On = true, Devices = [new Device { Key = "cGl4ZWw=", Name = "pixel", ApprovedAt = (ulong)Now }] },
        };

        Save(file, prints, settings);
        // Saving again replaces the file rather than failing on it.
        Save(file, prints, settings);
        var text = File.ReadAllText(file);
        var (saved, loaded) = BoardFile.Load(file, Now);

        Assert.False(File.Exists(BoardFile.TempFile(file)), "the temporary file is moved into place");
        Assert.Contains("\"version\": 3", text);
        Assert.Equal(Loaded.Ok.Instance, loaded);
        Assert.Equal(settings, saved.Settings);
        Assert.Equal(
            [
                new SavedPrint(Path.Combine(scratch.Path, "a.png"), Kind.Image, Origin.Folder, Now - 50, Now - 10),
                new SavedPrint(Path.Combine(scratch.Path, "b.png"), Kind.Image, Origin.Capture, Now - 20, null),
                new SavedPrint(Path.Combine(scratch.Path, "c.txt"), Kind.Note, Origin.Capture, Now - 30, null),
            ],
            saved.Prints);
    }

    [Fact]
    public void A_stale_temp_file_is_cleared_on_load()
    {
        using var scratch = new Scratch("stale-temp");
        var file = Path.Combine(scratch.Path, "board.json");
        Save(file, [], new TackSettings());
        File.WriteAllText(BoardFile.TempFile(file), "{ \"prints\": [");

        var (_, loaded) = BoardFile.Load(file, Now);

        Assert.Equal(Loaded.Ok.Instance, loaded);
        Assert.False(File.Exists(BoardFile.TempFile(file)));
    }

    [Fact]
    public void No_board_json_is_missing()
    {
        using var scratch = new Scratch("missing");

        var (saved, loaded) = BoardFile.Load(Path.Combine(scratch.Path, "board.json"), Now);

        Assert.Equal(Loaded.Missing.Instance, loaded);
        Assert.Empty(saved.Prints);
        Assert.False(loaded.MustNotSave);
    }

    [Theory]
    [InlineData("corrupt", """{ "prints": [ { "path": """)]
    [InlineData("newer", """{ "version": 4, "prints": [], "layout": "whatever comes next" }""")]
    public void An_unusable_board_json_is_backed_up_before_any_save(string name, string contents)
    {
        using var scratch = new Scratch(name);
        var file = Path.Combine(scratch.Path, "board.json");
        File.WriteAllText(file, contents);

        var (_, loaded) = BoardFile.Load(file, Now);
        // The app may now save over it; the original must survive.
        Save(file, [], new TackSettings());
        var backup = $"{file}.bad-{Now}";
        var (_, reloaded) = BoardFile.Load(file, Now);

        Assert.Equal(backup, Assert.IsType<Loaded.Unreadable>(loaded).Backup);
        Assert.False(loaded.MustNotSave);
        Assert.Equal(contents, File.ReadAllText(backup));
        Assert.Equal(Loaded.Ok.Instance, reloaded);
    }

    [Fact]
    public void A_save_during_the_restore_keeps_the_prints_not_restored_yet()
    {
        using var scratch = new Scratch("pending");
        var file = Path.Combine(scratch.Path, "board.json");
        Print[] onBoard = [Print(scratch.Path, "1", "a.png", Origin.Folder, Now - 10, null)];
        SavedPrint[] pending =
        [
            // Restored already: listed once.
            new(Path.Combine(scratch.Path, "A.PNG"), Kind.Image, Origin.Folder, Now - 10, null),
            new(Path.Combine(scratch.Path, "b.png"), Kind.Image, Origin.Capture, Now - 20, Now - 5),
            new(Path.Combine(scratch.Path, "c.txt"), Kind.Note, Origin.Capture, Now - 30, null),
        ];

        BoardFile.Write(file, Snapshot.Of(onBoard, pending, new TackSettings()));
        var (saved, loaded) = BoardFile.Load(file, Now);

        Assert.Equal(Loaded.Ok.Instance, loaded);
        SavedPrint[] expected =
        [
            new(Path.Combine(scratch.Path, "a.png"), Kind.Image, Origin.Folder, Now - 10, null),
            pending[1],
            pending[2],
        ];
        Assert.Equal(expected.OrderBy(p => (p.KeptAt, p.PinnedAt), History.RowOrder), saved.Prints);
    }
}
