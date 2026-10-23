using Tack.Core.Updates;

namespace Tack.Core.Tests.Updates;

/// <summary>The signed version, download progress and the tray's updates item.</summary>
public class UpdateSmallRulesTests
{
    [Theory]
    [InlineData("0.3.1", "0.3.1", true)]
    [InlineData("v0.3.1", "0.3.1", true)]
    [InlineData("0.3.1", "v0.3.1", true)]
    [InlineData("0.1.0", "9.9.9", false)]
    [InlineData("0.3.1-beta.1", "0.3.1", false)]
    [InlineData("0.3.1+a", "0.3.1", false)]
    // Not versions: compared as text.
    [InlineData("nightly", "nightly", true)]
    [InlineData("nightly", "0.3.1", false)]
    public void The_signed_version_must_be_the_announced_one(string signed, string announced, bool same) =>
        Assert.Equal(same, UpdateSignature.SameVersion(signed, announced));

    [Fact]
    public void Progress_is_whole_percent_when_the_size_is_known()
    {
        Assert.Equal(25, DownloadPercent.Of(25, 100));
        Assert.Equal(33, DownloadPercent.Of(2, 6));
        Assert.Equal(99, DownloadPercent.Of(999, 1000));
        Assert.Equal(100, DownloadPercent.Of(150, 100));
        Assert.Equal(100, DownloadPercent.Of(long.MaxValue, long.MaxValue));
        Assert.Null(DownloadPercent.Of(10, 0));
        Assert.Null(DownloadPercent.Of(10, null));
    }

    [Fact]
    public void The_tray_item_names_the_version_that_is_ready()
    {
        Assert.Equal("Check for updates…", UpdateWords.TrayLabel(UpdateStatus.Idle));
        var found = UpdateStatus.Found("0.3.1", null, 5);
        Assert.Equal("Update available: v0.3.1", UpdateWords.TrayLabel(found));
        Assert.Equal("Check for updates…", UpdateWords.TrayLabel(found with { State = UpdatePhase.Downloading }));
        Assert.Equal("Check for updates…", UpdateWords.TrayLabel(found.Failed(UpdateFailures.Network)));
        Assert.Equal("Check for updates…", UpdateWords.TrayLabel(new UpdateStatus { State = UpdatePhase.Available }));
    }
}
