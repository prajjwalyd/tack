using System.Runtime.InteropServices;
using System.Text.Json;

namespace Tack.Core.Updates.Feed;

/// <summary>
/// Reading latest.json as Tack 0.2 did: the version, the notes, and this
/// PC's installer, chosen by the process's architecture. Anything the feed
/// gets wrong is the server's failure (<see cref="UpdateFailure.Server"/>).
/// </summary>
public static class UpdateFeed
{
    /// <summary>The installer's file name when its URL gives no plain one.</summary>
    public const string FallbackInstallerName = "Tack-setup.exe";

    /// <summary>Reads <paramref name="json"/>; a feed with no version Tack can read is the server's failure.</summary>
    public static ReleaseFeed Read(string json)
    {
        ReleaseFeed? feed;
        try
        {
            feed = JsonSerializer.Deserialize(json, FeedJsonContext.Default.ReleaseFeed);
        }
        catch (JsonException e)
        {
            throw new UpdateFailureException(UpdateFailure.Server, $"latest.json is not JSON Tack reads: {e.Message}");
        }
        if (feed is null || !SemVersion.TryParse(feed.Version, out _))
        {
            throw new UpdateFailureException(UpdateFailure.Server, "latest.json has no version Tack reads");
        }
        return feed;
    }

    /// <summary>The platform latest.json lists for <paramref name="architecture"/>, or null for one Tack is not built for.</summary>
    public static string? PlatformFor(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "windows-x86_64",
        Architecture.Arm64 => "windows-aarch64",
        _ => null,
    };

    /// <summary>
    /// What to install when <paramref name="feed"/> is newer than
    /// <paramref name="current"/> (this build's version); null when it is not.
    /// As in Tack 0.2, this PC's installer must be listed, with an https URL
    /// and a signature, whether or not the version is newer.
    /// </summary>
    public static UpdateOffer? Offer(ReleaseFeed feed, string current, Architecture architecture)
    {
        if (!SemVersion.TryParse(current, out var running))
        {
            throw new UpdateFailureException(UpdateFailure.Other, $"this build's version {current} is not a version");
        }
        if (!SemVersion.TryParse(feed.Version, out var newest))
        {
            throw new UpdateFailureException(UpdateFailure.Server, "latest.json has no version Tack reads");
        }
        var platform = PlatformFor(architecture);
        if (platform is null || feed.Platforms?.GetValueOrDefault(platform) is not { } installer)
        {
            throw new UpdateFailureException(UpdateFailure.Server, $"latest.json lists no installer for {platform ?? architecture.ToString()}");
        }
        if (!Uri.TryCreate(installer.Url, UriKind.Absolute, out var url) || !HttpsRedirects.IsHttps(url))
        {
            throw new UpdateFailureException(UpdateFailure.Server, "the installer's URL is not https");
        }
        if (string.IsNullOrWhiteSpace(installer.Signature))
        {
            throw new UpdateFailureException(UpdateFailure.Server, "the installer has no signature");
        }
        return newest > running ? new UpdateOffer(newest.ToString(), feed.Notes, url, installer.Signature) : null;
    }

    /// <summary>
    /// The name to save the installer under: the last part of its URL when that
    /// is a plain .exe name (ASCII letters, digits, ".", "_" and "-"), as
    /// "Tack_0.3.1_x64-setup.exe"; otherwise <see cref="FallbackInstallerName"/>.
    /// </summary>
    public static string InstallerName(Uri url)
    {
        var name = url.Segments.Length > 0 ? Uri.UnescapeDataString(url.Segments[^1]) : string.Empty;
        var plain = name.Length is > 4 and <= 100
            && name[0] != '.'
            && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
        return plain ? name : FallbackInstallerName;
    }
}
