using Tack.Core.Prints;
using Tack.Core.Settings;
using Tack.Core.Storage;
using static Tack.Core.Tests.Storage.StorageFixture;

namespace Tack.Core.Tests.Storage;

/// <summary>Linked files in board.json: saved as "file", never as Tack's own, and skipped by builds that don't know them.</summary>
public class FilePrintStorageTests
{
    static Print Linked(string path, string id = "9", Origin origin = Origin.Folder, long pinnedAt = Now - 5, long? keptAt = null) => new()
    {
        Id = id,
        Path = path,
        Name = Path.GetFileName(path),
        Kind = Kind.File,
        File = new FileFacts(false, 12, "Text Document"),
        PinnedAt = pinnedAt,
        KeptAt = keptAt,
        Origin = origin,
    };

    [Fact]
    public void A_file_is_saved_with_its_kind_and_never_as_a_capture()
    {
        using var scratch = new Scratch("file-kind");
        var file = Path.Combine(scratch.Path, "board.json");

        // Even a print that somehow claims Tack's origin is written as the user's own.
        Save(file, [Linked(@"C:\Users\me\Report.docx", origin: Origin.Capture)], new TackSettings());

        var text = File.ReadAllText(file);
        Assert.Contains("\"kind\": \"file\"", text);
        Assert.Contains("\"capture\": false", text);
        Assert.DoesNotContain("\"capture\": true", text);
        // Same schema: an older Tack reads the board and skips only the kind it doesn't know.
        Assert.Contains("\"version\": 3", text);
    }

    [Fact]
    public void A_file_round_trips_kept_and_in_its_place()
    {
        using var scratch = new Scratch("file-round-trip");
        var file = Path.Combine(scratch.Path, "board.json");
        var note = Print(scratch.Path, "2", "c.txt", Origin.Capture, Now - 30, null);

        Save(file, [Linked(@"C:\Users\me\Projects\", keptAt: Now - 1), note], new TackSettings());
        var (saved, loaded) = BoardFile.Load(file, Now);

        Assert.Equal(Loaded.Ok.Instance, loaded);
        Assert.Equal(
            [
                new SavedPrint(@"C:\Users\me\Projects\", Kind.File, Origin.Folder, Now - 5, Now - 1),
                new SavedPrint(Path.Combine(scratch.Path, "c.txt"), Kind.Note, Origin.Capture, Now - 30, null),
            ],
            saved.Prints);
    }

    [Fact]
    public void A_file_marked_as_a_capture_by_hand_still_loads_as_the_users_own()
    {
        var (saved, _) = Parse("""
            { "version": 3, "prints": [ { "path": "C:\\Users\\me\\a.docx", "kind": "file", "capture": true, "pinnedAt": 9 } ] }
            """);

        var print = Assert.Single(saved.Prints);
        Assert.Equal((Kind.File, Origin.Folder), (print.Kind, print.Origin));
    }

    [Fact]
    public void A_file_still_waiting_to_be_restored_is_saved_again()
    {
        var pending = new SavedPrint(@"E:\usb\deck.pptx", Kind.File, Origin.Folder, Now - 3, null);

        var snapshot = Snapshot.Of([Linked(@"C:\Users\me\a.docx")], [pending], new TackSettings());

        Assert.Equal(["file", "file"], snapshot.Prints.Select(p => p.Kind));
        Assert.All(snapshot.Prints, p => Assert.False(p.Capture));
    }
}
