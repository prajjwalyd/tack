namespace Tack.Windows.Edge;

/// <summary>
/// The poller's timing, tuned to the wake-up budget. The reveal dwell and the
/// leave delay are the user's speed (<c>RevealSpeedTimes</c>); everything here
/// is class C: an input threshold, a physical gesture's time or polling the
/// system, which never scale.
/// </summary>
internal static class EdgeTiming
{
    /// <summary>Shown, and tucked with the pointer at the edge.</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(25);

    /// <summary>Tucked without edge strips: a look this often. Nothing tells then that the pointer is at the edge.</summary>
    public static readonly TimeSpan PollSlow = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Movement that still counts as resting at the top edge, px. At the slowest
    /// speed the dwell is long enough that reaching for a title bar or a browser
    /// tab, which passes through the edge too, does not count.
    /// </summary>
    public const int EdgeJitter = 3;

    /// <summary>
    /// How long something carried over the edge waits for the board: the user is
    /// waiting with it in hand. At most the edge's dwell: a drag never waits
    /// longer than the pointer would.
    /// </summary>
    public static readonly TimeSpan DragDwell = TimeSpan.FromMilliseconds(220);

    /// <summary>Slack around the board before the pointer counts as gone, logical px.</summary>
    public const double LeaveMargin = 10.0;

    /// <summary>SHQueryUserNotificationState is not free; nobody notices half a second.</summary>
    public static readonly TimeSpan FullScreenCheck = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The pointer cannot be read: another desktop has the input (a UAC prompt,
    /// or the lock screen). Look again this soon, <see cref="UnreadableRetries"/>
    /// times, then only every <see cref="PollUnreadableSlow"/>.
    /// </summary>
    public static readonly TimeSpan PollUnreadable = TimeSpan.FromSeconds(1);

    public const uint UnreadableRetries = 3;

    public static readonly TimeSpan PollUnreadableSlow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a tucked poller waits: as long as the watch says when the strips
    /// tell it about the edge, else a slow look at a time. Null: until told.
    /// </summary>
    public static TimeSpan? TuckedWait(bool strips, TimeSpan? nextLook) => strips ? nextLook : PollSlow;
}
