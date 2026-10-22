namespace Tack.Core.Settings;

/// <summary>Looking for new versions.</summary>
public sealed record UpdateSettings
{
    /// <summary>Look for a new version once when Tack starts, and say so when there is one.</summary>
    public bool Check { get; init; } = true;
}
