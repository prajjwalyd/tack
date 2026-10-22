namespace Tack.Windows.Edge;

/// <summary>The outcome of one look at the pointer.</summary>
/// <param name="Action">What the app is asked to do.</param>
/// <param name="ClickThrough">The board window's click-through changes to this, or stays as it is when null.</param>
/// <param name="Wait">How long until the next look; null to wait to be told.</param>
internal readonly record struct Decision(EdgeAction Action, bool? ClickThrough, TimeSpan? Wait)
{
    /// <summary>Nothing to do and nothing to watch until something is heard.</summary>
    public static Decision Park => new(EdgeAction.None, null, null);

    public static Decision After(TimeSpan wait) => new(EdgeAction.None, null, wait);
}
