using Tack.Windows.Clipboard;

namespace Tack.Windows.Tests.Clipboard;

public sealed class RestorePlanTests
{
    const uint Text = 13;
    static readonly uint[] s_markers = [100, 101, 102, 103];
    static readonly byte[] s_zero = [0, 0, 0, 0];
    static readonly byte[] s_words = [104, 0, 105, 0];

    [Fact]
    public void A_private_copy_goes_back_with_every_marker()
    {
        var plan = RestorePlan.Of([(Text, s_words), (100, [7, 7, 7, 7])], [true, false, false, false], s_markers);

        Assert.Equal(
            [
                new RestoreWrite(Text, s_words, true),
                new RestoreWrite(100, s_zero, true),
                new RestoreWrite(101, s_zero, true),
                new RestoreWrite(102, s_zero, true),
                new RestoreWrite(103, s_zero, true),
            ],
            plan,
            Same);
    }

    [Fact]
    public void A_plain_copy_goes_back_kept_out_of_history_and_the_cloud()
    {
        var plan = RestorePlan.Of([(Text, s_words)], [false, false, false, false], s_markers);

        Assert.Equal(
            [new RestoreWrite(Text, s_words, true), new RestoreWrite(102, s_zero, false), new RestoreWrite(103, s_zero, false)],
            plan,
            Same);
    }

    [Fact]
    public void A_copy_that_allows_history_keeps_its_own_say()
    {
        byte[] allowed = [1, 0, 0, 0];
        var plan = RestorePlan.Of([(Text, s_words), (102, allowed)], [false, false, false, false], s_markers);

        Assert.Equal(
            [new RestoreWrite(Text, s_words, true), new RestoreWrite(102, allowed, true), new RestoreWrite(103, s_zero, false)],
            plan,
            Same);
    }

    [Fact]
    public void An_empty_clipboard_goes_back_empty() =>
        Assert.Empty(RestorePlan.Of([], [false, false, false, false], s_markers));

    static bool Same(RestoreWrite a, RestoreWrite b) =>
        a.Format == b.Format && a.Counts == b.Counts && a.Bytes.SequenceEqual(b.Bytes);
}
