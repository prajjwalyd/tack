using Tack.Core.Updates;

namespace Tack.Core.Tests.Updates;

public class UpdateStatusTests
{
    [Fact]
    public void Notes_are_blank_cut_or_left_alone()
    {
        Assert.Null(UpdateStatus.CleanNotes(null));
        Assert.Null(UpdateStatus.CleanNotes(" \r\n\t"));
        const string Notes = "## Fixes\n- <b>one</b> & two\n";
        Assert.Equal(Notes, UpdateStatus.CleanNotes(Notes));

        var cut = UpdateStatus.CleanNotes(new string('é', UpdateStatus.NotesMaxChars + 50))!;
        Assert.Equal(UpdateStatus.NotesMaxChars + 1, cut.Length);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
    }

    [Fact]
    public void Notes_are_counted_in_characters_and_never_split_a_pair()
    {
        var face = char.ConvertFromUtf32(0x1F600);
        var notes = string.Concat(Enumerable.Repeat(face, UpdateStatus.NotesMaxChars + 1));

        var cut = UpdateStatus.CleanNotes(notes)!;

        Assert.Equal((UpdateStatus.NotesMaxChars * 2) + 1, cut.Length);
        Assert.Equal(notes[..(UpdateStatus.NotesMaxChars * 2)] + "…", cut);
        Assert.Equal(notes[..^2], UpdateStatus.CleanNotes(notes[..^2]));
    }

    [Fact]
    public void A_failure_keeps_the_last_good_time_and_drops_the_rest()
    {
        var found = UpdateStatus.Found("0.3.1", "new", 1_000);
        Assert.Equal((UpdatePhase.Available, 1_000L), (found.State, found.CheckedAt!.Value));

        var checking = found.Checking();
        Assert.Equal((UpdatePhase.Checking, null, null), (checking.State, checking.Version, checking.Notes));
        Assert.True(checking.Busy);

        var failed = checking.Failed("no luck");
        Assert.Equal((UpdatePhase.Error, "no luck", 1_000L), (failed.State, failed.Error, failed.CheckedAt!.Value));
        Assert.False(failed.Busy);
        Assert.Equal(2_000, UpdateStatus.UpToDate(2_000).CheckedAt);
    }

    [Fact]
    public void A_failed_install_keeps_the_version_but_not_the_notes()
    {
        var downloading = UpdateStatus.Found("0.3.1", "new", 5) with { State = UpdatePhase.Downloading, Progress = 0.4 };

        var failed = downloading.Failed("no luck");

        Assert.Equal(("0.3.1", null, null), (failed.Version, failed.Notes, failed.Progress));
    }

    [Fact]
    public void Nothing_is_checked_at_first()
    {
        Assert.Equal(UpdatePhase.Idle, UpdateStatus.Idle.State);
        Assert.Null(UpdateStatus.Idle.CheckedAt);
        Assert.False(UpdateStatus.Idle.Busy);
    }
}
