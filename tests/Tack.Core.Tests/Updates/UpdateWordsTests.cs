using System.Globalization;
using Tack.Core.Updates;

namespace Tack.Core.Tests.Updates;

public class UpdateWordsTests
{
    const long Now = 1_700_000_000_000;

    static UpdateView Describe(UpdateStatus status) =>
        UpdateWords.Describe(status, Now, CultureInfo.InvariantCulture, TimeZoneInfo.Utc);

    [Fact]
    public void Before_any_check_it_offers_one_with_no_line_under()
    {
        var view = Describe(UpdateStatus.Idle);

        Assert.Equal(new UpdateView("Check for updates", "", new("Check for updates", UpdateAction.Check)), view);
    }

    [Fact]
    public void A_finished_check_says_when_it_was()
    {
        var upToDate = Describe(UpdateStatus.UpToDate(Now - (5 * 60_000)));
        Assert.Equal(("Tack is up to date", "Last checked 5 minutes ago"), (upToDate.Title, upToDate.Sub));
        Assert.Equal(UpdateAction.Check, upToDate.Button.Act);

        var idle = Describe(UpdateStatus.Idle with { CheckedAt = Now - (3 * 86_400_000) });
        Assert.StartsWith("Last checked on ", idle.Sub, StringComparison.Ordinal);
        Assert.Equal("Check for updates", idle.Title);
    }

    [Fact]
    public void Checking_is_busy_with_nothing_to_press()
    {
        var view = Describe(UpdateStatus.UpToDate(Now).Checking());

        Assert.Equal(("Checking for updates…", ""), (view.Title, view.Sub));
        Assert.Equal(new UpdateButton("Checking…", UpdateAction.None, Busy: true), view.Button);
        Assert.Null(view.Meter);
    }

    [Fact]
    public void A_new_version_is_offered_to_install()
    {
        var view = Describe(UpdateStatus.Found("0.3.1", null, Now));

        Assert.Equal(("Version 0.3.1 is available", "Tack restarts to finish installing it."), (view.Title, view.Sub));
        Assert.Equal(new UpdateButton("Install", UpdateAction.Install, Accent: true), view.Button);
    }

    [Fact]
    public void A_download_shows_how_far_it_is_when_that_is_known()
    {
        var found = UpdateStatus.Found("0.3.1", null, Now);

        var known = Describe(found with { State = UpdatePhase.Downloading, Progress = 0.425 });
        Assert.Equal(("Downloading version 0.3.1…", "43%"), (known.Title, known.Sub));
        Assert.Equal(new UpdateMeter(0.425), known.Meter);
        Assert.True(known.Button.Busy);

        var unknown = Describe(found with { State = UpdatePhase.Downloading });
        Assert.Equal("", unknown.Sub);
        Assert.Equal(new UpdateMeter(null), unknown.Meter);
    }

    [Fact]
    public void Installing_slides_and_says_Tack_restarts()
    {
        var view = Describe(UpdateStatus.Found("0.3.1", null, Now) with { State = UpdatePhase.Installing });

        Assert.Equal(("Installing version 0.3.1…", "Tack restarts when it is done."), (view.Title, view.Sub));
        Assert.Equal(new UpdateButton("Installing…", UpdateAction.None, Busy: true), view.Button);
        Assert.Equal(new UpdateMeter(null), view.Meter);
    }

    [Fact]
    public void An_error_says_why_or_to_try_later_and_offers_to_try_again()
    {
        var why = Describe(UpdateStatus.Idle.Failed("The update server didn't answer."));
        Assert.Equal(("Couldn't check for updates", "The update server didn't answer."), (why.Title, why.Sub));
        Assert.Equal(new UpdateButton("Try again", UpdateAction.Check), why.Button);

        var later = Describe(UpdateStatus.Idle with { State = UpdatePhase.Error });
        Assert.Equal("Try again in a little while.", later.Sub);
        Assert.Equal("Couldn't check for updates. Try again in a little while.",
            UpdateWords.Announcement(UpdateStatus.Idle with { State = UpdatePhase.Error }, later));
    }

    [Fact]
    public void A_change_of_phase_is_announced_by_its_headline()
    {
        var status = UpdateStatus.UpToDate(Now);

        Assert.Equal("Tack is up to date", UpdateWords.Announcement(status, Describe(status)));
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.005, 1)]
    [InlineData(0.994, 99)]
    [InlineData(1.0, 100)]
    public void Percentages_round_halves_up(double fraction, int percent) =>
        Assert.Equal(percent, UpdateWords.Percent(fraction));
}
