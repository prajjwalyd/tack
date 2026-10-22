using Tack.Core.Phone;

namespace Tack.Core.Tests.Phone;

public class PhoneCardWordsTests
{
    static readonly PhoneState s_off = new();
    static readonly PhoneState s_serving = new() { On = true, Serving = true, Address = "http://my-pc.netbird.cloud:7717" };

    static Device DeviceOf(string key) => new() { Key = key, Name = key, ApprovedAt = 0 };

    static Asking AskingOf(string key, string name) => new(key, name, $"{name}.netbird.cloud", "100.90.1.2", "4821", 0);

    [Fact]
    public void The_card_offers_to_set_up_while_the_phone_link_is_off()
    {
        Assert.Equal(new PhoneCardView("Off", PhoneTone.None, "Set up"), PhoneWords.Card(s_off));
        Assert.True(PhoneWords.Card(s_off).Accent);
    }

    [Fact]
    public void The_card_counts_the_allowed_devices_once_the_board_is_being_served()
    {
        Assert.Equal("On · no devices yet", PhoneWords.Card(s_serving).Status);
        Assert.Equal("On · 1 device", PhoneWords.Card(s_serving with { Devices = [DeviceOf("a")] }).Status);
        Assert.Equal("On · 2 devices", PhoneWords.Card(s_serving with { Devices = [DeviceOf("a"), DeviceOf("b")] }).Status);
        Assert.Equal("Manage", PhoneWords.Card(s_serving).Button);
        Assert.False(PhoneWords.Card(s_serving).Accent);
        Assert.Equal(PhoneTone.Ok, PhoneWords.Card(s_serving).Tone);
    }

    [Fact]
    public void The_card_says_what_NetBird_needs_in_the_phone_pages_words()
    {
        Assert.Equal("NetBird isn't installed", PhoneWords.Card(s_off with { NetBird = NetBirdState.Missing }).Status);
        var disconnected = PhoneWords.Card(new PhoneState { On = true, NetBird = NetBirdState.Disconnected });
        Assert.Equal(("NetBird isn't connected", PhoneTone.Warn), (disconnected.Status, disconnected.Tone));
        // Off, NetBird is only a problem if it is not there at all.
        Assert.Equal("Off", PhoneWords.Card(s_off with { NetBird = NetBirdState.Disconnected }).Status);
    }

    [Fact]
    public void A_device_asking_comes_first_then_a_failure_to_start()
    {
        var pixel = AskingOf("k", "pixel");

        var one = PhoneWords.Card(s_serving with { Pending = [pixel] });
        Assert.Equal(("pixel wants to use your board", PhoneTone.Attention), (one.Status, one.Tone));
        Assert.Equal("2 devices want to use your board",
            PhoneWords.Card(s_serving with { Pending = [pixel, pixel with { Key = "j" }] }).Status);

        var failed = PhoneWords.Card(new PhoneState { On = true, Error = "Couldn't start: port 7717 is in use" });
        Assert.Equal(("Couldn't start: port 7717 is in use", PhoneTone.Warn), (failed.Status, failed.Tone));
        Assert.Equal(new PhoneCardView("Getting ready…", PhoneTone.None, "Manage"), PhoneWords.Card(new PhoneState { On = true }));
    }

    [Fact]
    public void Nothing_is_known_until_the_state_is()
    {
        var card = PhoneWords.Card(null);

        Assert.Equal("Set up", card.Button);
        Assert.Contains("NetBird", card.Status, StringComparison.Ordinal);
        Assert.Equal("Use on your phone: Off", PhoneWords.CardAnnouncement(PhoneWords.Card(s_off)));
    }

    [Fact]
    public void The_saved_settings_alone_give_a_connected_board_not_yet_served()
    {
        var saved = new PhoneSettings { On = true }.Allow("k", "pixel", 5);

        var state = SavedPhoneLink.Instance.State(saved);

        Assert.Equal((true, NetBirdState.Connected, false), (state.On, state.NetBird, state.Serving));
        Assert.Equal(saved.Devices, state.Devices);
        Assert.Empty(state.Pending);
        Assert.Equal("Getting ready…", PhoneWords.Card(state).Status);
        Assert.Equal("Off", PhoneWords.Card(SavedPhoneLink.Instance.State(new PhoneSettings())).Status);
        Assert.False(SavedPhoneLink.Instance.Answer("k", true));
    }
}
