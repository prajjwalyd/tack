using Tack.Windows.Clipboard;
using Tack.Windows.Imaging;
using Tack.Windows.Snips;

namespace Tack.Windows.Tests.Snips;

public sealed class PictureTakerTests
{
    static readonly CopiedPicture s_picture = new(CopiedFormat.Png, [1]);

    static Func<(CopiedPicture?, Untaken)> Attempts(params (CopiedPicture?, Untaken)[] results)
    {
        var next = 0;
        return () => results[Math.Min(next++, results.Length - 1)];
    }

    [Theory]
    [InlineData(Untaken.Replaced)]
    [InlineData(Untaken.Private)]
    public async Task A_capture_replaced_or_marked_private_is_given_up_at_once(Untaken why)
    {
        var calls = 0;
        var taken = await PictureTaker.TakeAsync(() => { calls++; return (null, why); }, 6, TimeSpan.Zero);
        Assert.Equal((null, why), taken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_busy_or_unreadable_clipboard_is_tried_again()
    {
        var attempt = Attempts((null, Untaken.Busy), (null, Untaken.Unreadable), (s_picture, Untaken.Busy));
        var (picture, _) = await PictureTaker.TakeAsync(attempt, 6, TimeSpan.Zero);
        Assert.Same(s_picture, picture);
    }

    [Fact]
    public async Task After_the_last_try_it_says_why()
    {
        var unreadable = Attempts((null, Untaken.Unreadable), (null, Untaken.Busy));
        Assert.Equal((null, Untaken.Unreadable), await PictureTaker.TakeAsync(unreadable, 6, TimeSpan.Zero));

        var busy = Attempts((null, Untaken.Busy));
        Assert.Equal((null, Untaken.Busy), await PictureTaker.TakeAsync(busy, 6, TimeSpan.Zero));

        var late = Attempts((null, Untaken.Busy), (null, Untaken.Busy), (s_picture, Untaken.Busy));
        Assert.Null((await PictureTaker.TakeAsync(late, 2, TimeSpan.Zero)).Picture);
    }
}
