using System.Runtime.InteropServices;
using Tack.Core.Updates;
using Tack.Core.Updates.Feed;

namespace Tack.Core.Tests.Updates;

public class UpdateFeedTests
{
    /// <summary>Tack 0.2.0's own latest.json, as its release published it.</summary>
    static string RealFeed() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "latest-v0.2.0.json"));

    static ReleaseFeed Feed(string version, string url = "https://example.test/Tack_x64-setup.exe", string? signature = "c2ln") => new()
    {
        Version = version,
        Notes = "new",
        Platforms = new() { ["windows-x86_64"] = new FeedPlatform { Url = url, Signature = signature } },
    };

    [Fact]
    public void The_real_feed_is_read()
    {
        var feed = UpdateFeed.Read(RealFeed());

        Assert.Equal("0.2.0", feed.Version);
        Assert.Equal("", feed.Notes);
        Assert.Equal(4, feed.Platforms!.Count);
        var x64 = feed.Platforms["windows-x86_64"];
        Assert.Equal("https://github.com/prajjwalyd/tack/releases/download/v0.2.0/Tack_0.2.0_x64-setup.exe", x64.Url);
        Assert.StartsWith("dW50cnVzdGVkIGNvbW1lbnQ6", x64.Signature, StringComparison.Ordinal);
    }

    [Fact]
    public void The_real_feed_offers_its_installer_to_an_older_tack_only()
    {
        var feed = UpdateFeed.Read(RealFeed());

        var offer = UpdateFeed.Offer(feed, "0.1.9", Architecture.Arm64)!;
        Assert.Equal("0.2.0", offer.Version);
        Assert.Null(UpdateStatus.CleanNotes(offer.Notes));
        Assert.Equal("https://github.com/prajjwalyd/tack/releases/download/v0.2.0/Tack_0.2.0_arm64-setup.exe", offer.Url.ToString());
        Assert.Equal("Tack_0.2.0_arm64-setup.exe", UpdateFeed.InstallerName(offer.Url));

        Assert.Null(UpdateFeed.Offer(feed, "0.2.0", Architecture.X64));
        Assert.Null(UpdateFeed.Offer(feed, "0.3.0", Architecture.X64));
    }

    [Fact]
    public void The_platform_follows_the_architecture()
    {
        Assert.Equal("windows-x86_64", UpdateFeed.PlatformFor(Architecture.X64));
        Assert.Equal("windows-aarch64", UpdateFeed.PlatformFor(Architecture.Arm64));
        Assert.Null(UpdateFeed.PlatformFor(Architecture.X86));
    }

    [Fact]
    public void A_newer_version_is_offered_without_its_v()
    {
        var offer = UpdateFeed.Offer(Feed("v0.3.1"), "0.3.0", Architecture.X64)!;
        Assert.Equal(("0.3.1", "new", "c2ln"), (offer.Version, offer.Notes, offer.Signature));
        // A pre-release is below its release.
        Assert.Null(UpdateFeed.Offer(Feed("0.3.0-beta.1"), "0.3.0", Architecture.X64));
        Assert.NotNull(UpdateFeed.Offer(Feed("0.3.0"), "0.3.0-beta.1", Architecture.X64));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\": \"soon\"}")]
    [InlineData("{\"version\": 3}")]
    public void A_feed_without_a_readable_version_is_the_servers_failure(string json)
    {
        var failure = Assert.Throws<UpdateFailureException>(() => UpdateFeed.Read(json));
        Assert.Equal(UpdateFailure.Server, failure.Kind);
    }

    [Fact]
    public void This_pcs_installer_must_be_listed_over_https_and_signed()
    {
        void Server(ReleaseFeed feed, Architecture architecture = Architecture.X64) =>
            Assert.Equal(UpdateFailure.Server, Assert.Throws<UpdateFailureException>(() => UpdateFeed.Offer(feed, "0.3.0", architecture)).Kind);

        Server(Feed("0.4.0"), Architecture.Arm64);
        Server(Feed("0.4.0", url: "http://example.test/Tack_x64-setup.exe"));
        Server(Feed("0.4.0", url: "file:///C:/Windows/notepad.exe"));
        Server(Feed("0.4.0", url: "Tack_x64-setup.exe"));
        Server(Feed("0.4.0", signature: null));
        Server(Feed("0.4.0", signature: " "));
        Server(new ReleaseFeed { Version = "0.4.0" });
        // As in 0.2, even when nothing is newer.
        Server(Feed("0.2.0", url: "http://example.test/Tack_x64-setup.exe"));
    }

    [Theory]
    [InlineData("https://example.test/download/v0.3.1/Tack_0.3.1_x64-setup.exe", "Tack_0.3.1_x64-setup.exe")]
    [InlineData("https://example.test/a/Tack.EXE?x=1", "Tack.EXE")]
    [InlineData("https://example.test/", UpdateFeed.FallbackInstallerName)]
    [InlineData("https://example.test/Tack.msi", UpdateFeed.FallbackInstallerName)]
    [InlineData("https://example.test/..%5C..%5Cevil.exe", UpdateFeed.FallbackInstallerName)]
    [InlineData("https://example.test/a%20b.exe", UpdateFeed.FallbackInstallerName)]
    [InlineData("https://example.test/.exe", UpdateFeed.FallbackInstallerName)]
    public void The_installer_is_saved_under_a_plain_name(string url, string name) =>
        Assert.Equal(name, UpdateFeed.InstallerName(new Uri(url)));
}
