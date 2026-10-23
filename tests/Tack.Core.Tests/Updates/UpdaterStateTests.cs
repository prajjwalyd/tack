using Tack.Core.Updates;

namespace Tack.Core.Tests.Updates;

public class UpdaterStateTests
{
    static readonly UpdateOffer s_offer = new("0.3.1", "new", new Uri("https://example.test/Tack.exe"), "c2ln");

    static UpdaterState In(UpdatePhase phase) => new(new UpdateStatus { State = phase });

    [Theory]
    [InlineData(UpdatePhase.Idle, false, true)]
    [InlineData(UpdatePhase.Error, false, true)]
    [InlineData(UpdatePhase.UpToDate, false, true)]
    [InlineData(UpdatePhase.Available, false, true)]
    [InlineData(UpdatePhase.Checking, false, false)]
    [InlineData(UpdatePhase.Downloading, false, false)]
    [InlineData(UpdatePhase.Installing, false, false)]
    [InlineData(UpdatePhase.Idle, true, true)]
    // The automatic one runs only while nothing has been checked.
    [InlineData(UpdatePhase.Error, true, false)]
    [InlineData(UpdatePhase.UpToDate, true, false)]
    [InlineData(UpdatePhase.Available, true, false)]
    [InlineData(UpdatePhase.Checking, true, false)]
    [InlineData(UpdatePhase.Downloading, true, false)]
    public void A_check_starts_unless_one_or_an_install_is_going(UpdatePhase phase, bool automatic, bool starts)
    {
        var state = In(phase);
        Assert.Equal(starts, state.BeginCheck(automatic, settingOn: true));
        Assert.Equal(starts ? UpdatePhase.Checking : phase, state.Status.State);
    }

    [Fact]
    public void The_automatic_check_is_skipped_when_the_setting_is_off()
    {
        var state = In(UpdatePhase.Idle);
        Assert.False(state.BeginCheck(automatic: true, settingOn: false));
        Assert.Equal(UpdatePhase.Idle, state.Status.State);
        // The person's own check ignores the setting.
        Assert.True(state.BeginCheck(automatic: false, settingOn: false));
        Assert.Equal(UpdatePhase.Checking, state.Status.State);
    }

    [Fact]
    public void A_new_check_drops_the_version_the_last_one_found()
    {
        var state = new UpdaterState();
        state.Checked(s_offer, 5);
        Assert.True(state.BeginCheck(automatic: false, settingOn: true));
        Assert.Equal((null, 5L), (state.Status.Version, state.Status.CheckedAt!.Value));
        // And what it would have installed.
        state.Checked(null, 6);
        Assert.Null(state.BeginInstall());
    }

    [Fact]
    public void A_check_ends_up_to_date_or_with_a_version_to_install()
    {
        var state = new UpdaterState();
        state.BeginCheck(automatic: true, settingOn: true);
        state.Checked(null, 7);
        Assert.Equal(UpdateStatus.UpToDate(7), state.Status);

        state.BeginCheck(automatic: false, settingOn: true);
        state.Checked(s_offer, 8);
        Assert.Equal(UpdateStatus.Found("0.3.1", "new", 8), state.Status);
    }

    [Fact]
    public void An_install_takes_the_version_found_through_download_to_installing()
    {
        var state = new UpdaterState();
        Assert.Null(state.BeginInstall());
        Assert.False(state.Downloaded(50));
        state.Checked(s_offer, 8);

        Assert.Same(s_offer, state.BeginInstall());
        Assert.Equal((UpdatePhase.Downloading, "0.3.1", null), (state.Status.State, state.Status.Version, state.Status.Progress));
        // Taken once: a second press installs nothing more.
        Assert.Null(state.BeginInstall());
        Assert.False(state.BeginCheck(automatic: false, settingOn: true));

        Assert.True(state.Downloaded(42));
        Assert.Equal(0.42, state.Status.Progress);
        state.Installing();
        Assert.Equal((UpdatePhase.Installing, 1.0), (state.Status.State, state.Status.Progress!.Value));
        Assert.False(state.Downloaded(10));
    }

    [Fact]
    public void A_failed_install_keeps_the_version_and_has_to_find_it_again()
    {
        var state = new UpdaterState();
        state.Checked(s_offer, 8);
        state.BeginInstall();
        state.Downloaded(30);

        state.Fail(UpdateFailures.Signature);

        Assert.Equal((UpdatePhase.Error, "0.3.1", null, null), (state.Status.State, state.Status.Version, state.Status.Notes, state.Status.Progress));
        Assert.Equal(UpdateFailures.Signature, state.Status.Error);
        Assert.Equal(8, state.Status.CheckedAt);
        Assert.Null(state.BeginInstall());
        Assert.True(state.BeginCheck(automatic: false, settingOn: true));
    }

    [Fact]
    public void A_failed_check_keeps_the_last_good_time()
    {
        var state = new UpdaterState();
        state.Checked(null, 3);
        state.BeginCheck(automatic: false, settingOn: true);

        state.Fail(UpdateFailures.Network);

        Assert.Equal((UpdatePhase.Error, null, 3L), (state.Status.State, state.Status.Version, state.Status.CheckedAt!.Value));
    }
}
