using Tack.Windows.Clipboard;

namespace Tack.Windows.Snips;

/// <summary>
/// Tries to take a capture off the clipboard a few times, a gap apart, waiting
/// without holding a thread. A capture that was replaced or marked private is
/// given up at once; a busy clipboard or an app that has not drawn its picture
/// yet is tried again.
/// </summary>
static class PictureTaker
{
    /// <param name="attempt">One try; its reason counts only when it gives no picture.</param>
    public static async Task<(CopiedPicture? Picture, Untaken Why)> TakeAsync(
        Func<(CopiedPicture? Picture, Untaken Why)> attempt,
        int tries,
        TimeSpan gap)
    {
        var why = Untaken.Busy;
        for (var i = 0; i < tries; i++)
        {
            if (i > 0)
            {
                await Task.Delay(gap).ConfigureAwait(false);
            }
            var (picture, reason) = attempt();
            if (picture != null)
            {
                return (picture, why);
            }
            switch (reason)
            {
                case Untaken.Replaced or Untaken.Private:
                    return (null, reason);
                case Untaken.Unreadable:
                    why = reason;
                    break;
            }
        }
        return (null, why);
    }
}
