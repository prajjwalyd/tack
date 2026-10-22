using Tack.Core.Captures;

namespace Tack.Core.Tests.Captures;

public class PendingSavesTests
{
    const string Path = @"C:\Tack\Captures\Screenshot 2026-01-02 030405.png";

    [Fact]
    public void A_file_not_being_saved_is_handled_at_once()
    {
        var saves = new PendingSaves();
        string? handled = null;

        saves.WhenSaved(Path, path => handled = path);

        Assert.Equal(Path, handled);
        Assert.False(saves.IsSaving(Path));
    }

    [Fact]
    public void A_capture_being_saved_is_handled_once_it_is_written()
    {
        var saves = new PendingSaves();
        var handled = new List<string>();
        saves.Begin(Path);

        saves.WhenSaved(Path, handled.Add);
        saves.WhenSaved(Path, handled.Add);
        var waiting = (saves.IsSaving(Path), handled.Count);
        saves.Finish(Path);

        Assert.Equal((true, 0), waiting);
        Assert.Equal([Path, Path], handled);
        Assert.False(saves.IsSaving(Path));
    }

    [Fact]
    public void Paths_compare_ignoring_case()
    {
        var saves = new PendingSaves();
        saves.Begin(Path);

        Assert.True(saves.IsSaving(Path.ToUpperInvariant()));
        saves.Finish(Path.ToLowerInvariant());
        Assert.False(saves.IsSaving(Path));
    }

    [Fact]
    public async Task Waiting_ends_when_the_file_is_written()
    {
        var saves = new PendingSaves();
        saves.Begin(Path);

        var waiting = saves.WaitAsync(Path, TimeSpan.FromMinutes(1));
        var before = waiting.IsCompleted;
        saves.Finish(Path);
        await waiting.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(before);
    }

    [Fact]
    public async Task Waiting_gives_up_after_its_patience()
    {
        var saves = new PendingSaves();
        saves.Begin(Path);

        await saves.WaitAsync(Path, TimeSpan.FromMilliseconds(20)).WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(saves.IsSaving(Path));
    }

    [Fact]
    public async Task Waiting_for_a_file_not_being_saved_ends_at_once()
    {
        var saves = new PendingSaves();

        var waiting = saves.WaitAsync(Path, TimeSpan.FromMinutes(1));

        Assert.True(waiting.IsCompleted);
        await waiting;
    }
}
