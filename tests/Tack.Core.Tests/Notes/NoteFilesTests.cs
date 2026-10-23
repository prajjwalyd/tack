using System.Text;
using Tack.Core.Captures;
using Tack.Core.Notes;
using Tack.Core.Tests.Files;

namespace Tack.Core.Tests.Notes;

public class NoteFilesTests
{
    static readonly Timestamp At = new(2026, 10, 8, 14, 15, 30);

    [Fact]
    public void Notes_are_saved_with_windows_line_breaks_and_read_back()
    {
        using var scratch = new Scratch("note-save");
        var note = NoteText.Body("first line\nsecond line")!;

        var first = NoteFiles.Save(note, scratch.Path, At);
        var second = NoteFiles.Save(note, scratch.Path, At);

        Assert.Equal("Note 2026-10-08 141530.txt", Path.GetFileName(first));
        Assert.Equal("Note 2026-10-08 141530 (2).txt", Path.GetFileName(second));
        Assert.Equal("first line\r\nsecond line"u8.ToArray(), File.ReadAllBytes(first));
        Assert.Equal(note, NoteFiles.Read(first));
    }

    [Fact]
    public void Reading_tolerates_a_bom_bad_bytes_and_an_emptied_file()
    {
        Assert.Equal("https://example.org", NoteFiles.FromBytes([0xEF, 0xBB, 0xBF, .. "https://example.org\r\n"u8]).Link);
        Assert.Equal("caf\uFFFD", NoteFiles.FromBytes([.. "caf"u8, 0xE9]).Text);
        var empty = NoteFiles.FromBytes([]);
        Assert.Equal("", empty.Text);
        Assert.False(empty.Truncated);
    }

    [Fact]
    public void The_sweep_takes_old_unpinned_notes_only()
    {
        using var scratch = new Scratch("note-sweep");
        var pinned = scratch.File("Note 2026-10-08 141530.txt", "x");
        var leftover = scratch.File("Note 2026-10-07 101010.txt", "x");
        var partial = scratch.File("Note 2026-10-07 101011.txt.part", "x");
        scratch.File("not ours.png", "x");
        var later = Sweep.Cutoff(DateTime.UtcNow.AddDays(9), boardSaved: null);

        var swept = NoteFiles.Sweep(scratch.Path, [pinned], later, new NotingBin());

        Assert.Equal([leftover, partial], swept.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Only_a_text_file_directly_in_the_notes_folder_is_a_note()
    {
        using var scratch = new Scratch("note-folder");
        var folder = scratch.Folder("Notes");

        Assert.True(NoteFiles.IsIn(Path.Combine(folder, "Note 2026-10-08 141530.txt"), folder));
        Assert.True(NoteFiles.IsIn(Path.Combine(folder, "NOTE.TXT"), folder));
        Assert.False(NoteFiles.IsIn(Path.Combine(folder, "Note.png"), folder));
        Assert.False(NoteFiles.IsIn(Path.Combine(folder, "sub", "Note.txt"), folder));
        Assert.False(NoteFiles.IsIn(Path.Combine(folder, "..", "Notes", "Note.txt"), folder));
        Assert.False(NoteFiles.IsIn(@"C:\Windows\win.ini.txt", folder));
    }

    [Fact]
    public void A_note_held_open_for_writing_and_deleting_still_reads()
    {
        using var scratch = new Scratch("note-shared");
        var path = scratch.File("Note.txt", "being edited");

        // As an editor holds it: open for writing, letting others read, write and delete.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
            Assert.Equal("being edited", NoteFiles.Read(path).Text);
        }
    }

    [Fact]
    public void A_huge_file_is_read_only_as_far_as_a_note_goes()
    {
        using var scratch = new Scratch("note-huge");
        var path = scratch.File("Note.txt", new string('a', 5 * 1024 * 1024));

        var note = NoteFiles.Read(path);

        Assert.True(note.Truncated);
        Assert.Equal(NoteText.MaxNoteBytes, Encoding.UTF8.GetByteCount(note.Text));
    }
}
