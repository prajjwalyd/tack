namespace Tack.Core.Updates.Feed;

/// <summary>
/// latest.json, as Tack's release workflow writes it and Tack 0.2 read it:
/// the newest version, its notes, and an installer and its signature for each
/// platform ("windows-x86_64", "windows-aarch64"). It is the update server's
/// text, so everything is optional here and checked by <see cref="UpdateFeed"/>.
/// </summary>
public sealed record ReleaseFeed
{
    /// <summary>The newest version, as "0.3.1"; a leading "v" is allowed.</summary>
    public string? Version { get; init; }

    /// <summary>The release notes: untrusted text.</summary>
    public string? Notes { get; init; }

    /// <summary>Each platform's installer, by name.</summary>
    public Dictionary<string, FeedPlatform>? Platforms { get; init; }
}
