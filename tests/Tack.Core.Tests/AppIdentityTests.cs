using Tack.Core;

namespace Tack.Core.Tests;

public class AppIdentityTests
{
    [Fact]
    public void The_release_names_are_the_ones_tack_0_2_used()
    {
        var release = AppIdentity.For(development: false);

        Assert.Equal(@"Local\app.tack.board", release.MutexName);
        Assert.Equal("Tack", release.RunValueName);
    }

    [Fact]
    public void A_development_build_shares_no_name_with_the_installed_tack()
    {
        var release = AppIdentity.Release;
        var development = AppIdentity.For(development: true);

        Assert.NotEqual(release.MutexName, development.MutexName);
        Assert.NotEqual(release.RunValueName, development.RunValueName);
        Assert.NotEqual(release.DisplayName, development.DisplayName);
    }
}
