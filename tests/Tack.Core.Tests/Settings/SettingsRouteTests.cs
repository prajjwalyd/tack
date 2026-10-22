using Tack.Core.Settings;

namespace Tack.Core.Tests.Settings;

/// <summary>Where a link into Settings goes: the phone page, or a group of the list.</summary>
public class SettingsRouteTests
{
    [Fact]
    public void A_plain_link_opens_the_list_at_its_top()
    {
        Assert.Equal(new SettingsRoute(false, null), SettingsRoute.Of(""));
        Assert.Equal(new SettingsRoute(false, null), SettingsRoute.Of("#"));
        Assert.Equal(new SettingsRoute(false, null), SettingsRoute.Of(null));
    }

    [Fact]
    public void Phone_is_the_phone_page_with_or_without_the_hash()
    {
        Assert.Equal(new SettingsRoute(true, null), SettingsRoute.Of("#phone"));
        Assert.Equal(new SettingsRoute(true, null), SettingsRoute.Of("phone"));
    }

    [Fact]
    public void Any_other_id_is_a_group_of_the_list()
    {
        Assert.Equal(new SettingsRoute(false, "shortcuts"), SettingsRoute.Of("#shortcuts"));
        Assert.Equal(new SettingsRoute(false, "appearance"), SettingsRoute.Of("appearance"));
        Assert.Equal(new SettingsRoute(false, "updates"), SettingsRoute.Of("#updates"));
        Assert.Equal(new SettingsRoute(false, "shortcuts"), SettingsRoute.Of("#%73hortcuts"));
    }

    [Fact]
    public void An_id_the_list_does_not_have_stays_a_section_to_ignore_and_a_bad_escape_does_not_throw()
    {
        Assert.Equal(new SettingsRoute(false, "nowhere"), SettingsRoute.Of("#nowhere"));
        Assert.Equal(new SettingsRoute(false, "%E0%A4%A"), SettingsRoute.Of("#%E0%A4%A"));
    }
}
