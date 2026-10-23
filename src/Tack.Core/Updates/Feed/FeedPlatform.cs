namespace Tack.Core.Updates.Feed;

/// <summary>One platform's installer in latest.json.</summary>
public sealed record FeedPlatform
{
    /// <summary>Where to download the installer; it must be https.</summary>
    public string? Url { get; init; }

    /// <summary>The installer's minisign signature: the base64 of the .sig text.</summary>
    public string? Signature { get; init; }
}
