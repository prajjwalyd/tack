namespace Tack.Windows.Selection;

/// <summary>Where waiting for the pin shortcut's keys to be let go stands.</summary>
enum Release
{
    /// <summary>Every modifier has been up for a moment: send the copy keys.</summary>
    Ready,

    /// <summary>Look again shortly.</summary>
    Wait,

    /// <summary>Still held after <see cref="ReleaseWait.Patience"/>: give up, sending nothing.</summary>
    TimedOut,
}
