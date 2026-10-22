using Tack.Core.Phone;

namespace Tack.Core.Tests.Phone;

public class PhoneGateTests
{
    const string Pixel = "cGl4ZWw=";
    const string Laptop = "bGFwdG9w";

    static Who WhoIs(string key, string name) => new(key, name, $"{name}.netbird.cloud", "100.90.1.2");

    static Verdict Waiting(bool asked, string code) => new Verdict.Waiting(asked, code);

    [Fact]
    public void A_new_device_is_asked_about_once_with_one_code()
    {
        var (gate, settings) = (new PhoneGate(), new PhoneSettings());

        Assert.Equal(Waiting(true, "1234"), gate.Check(settings, WhoIs(Pixel, "pixel"), "1234", 0));
        Assert.Equal(Waiting(false, "1234"), gate.Check(settings, WhoIs(Pixel, "pixel"), "9999", 1_000));
        Assert.Single(gate.Pending);
        Assert.Equal("pixel.netbird.cloud", gate.Pending[0].Fqdn);
    }

    [Fact]
    public void Allowing_lets_the_device_in_by_its_key()
    {
        var (gate, settings) = (new PhoneGate(), new PhoneSettings());
        gate.Check(settings, WhoIs(Pixel, "pixel"), "1234", 0);

        Assert.Equal("pixel", gate.Answer(Pixel, true, 5));
        settings = settings.Allow(Pixel, "pixel", 5);

        Assert.Equal(Verdict.Allowed.Instance, gate.Check(settings, WhoIs(Pixel, "renamed"), "0000", 10));
        Assert.True(settings.Allows(Pixel));
        // A name is not an identity.
        Assert.Equal(Waiting(true, "5678"), gate.Check(settings, WhoIs(Laptop, "pixel"), "5678", 10));
    }

    [Fact]
    public void Each_refusal_makes_the_next_ask_wait_longer()
    {
        var (gate, settings) = (new PhoneGate(), new PhoneSettings());
        var pixel = WhoIs(Pixel, "pixel");
        gate.Check(settings, pixel, "1", 0);
        gate.Answer(Pixel, false, 0);

        Assert.Equal(Verdict.Refused.Instance, gate.Check(settings, pixel, "2", 1_000));
        Assert.Equal(Verdict.Refused.Instance, gate.AskAgain(settings, pixel, "2", 1_000));
        Assert.Equal(Waiting(true, "3"), gate.AskAgain(settings, pixel, "3", PhoneGate.AskAgainAfterMs));

        const long T = PhoneGate.AskAgainAfterMs;
        gate.Answer(Pixel, false, T);
        // Twice as long now.
        Assert.Equal(Verdict.Refused.Instance, gate.AskAgain(settings, pixel, "4", T + PhoneGate.AskAgainAfterMs));
        Assert.Equal(Waiting(true, "5"), gate.AskAgain(settings, pixel, "5", T + 2 * PhoneGate.AskAgainAfterMs));
    }

    [Fact]
    public void Refusals_outlive_the_server_stopping()
    {
        var (gate, settings) = (new PhoneGate(), new PhoneSettings());
        gate.Check(settings, WhoIs(Pixel, "pixel"), "1", 0);
        gate.Answer(Pixel, false, 0);

        gate.Clear();

        Assert.Equal(Verdict.Refused.Instance, gate.Check(settings, WhoIs(Pixel, "pixel"), "2", 1));
    }

    [Fact]
    public void Only_a_few_devices_may_wait_at_once()
    {
        var (gate, settings) = (new PhoneGate(), new PhoneSettings());
        for (var n = 0; n < PhoneGate.MaxPending; n++)
        {
            Assert.Equal(Waiting(true, "1"), gate.Check(settings, WhoIs($"k{n}", "x"), "1", 0));
        }

        Assert.Equal(Verdict.Busy.Instance, gate.Check(settings, WhoIs("one-more", "x"), "1", 0));
    }

    [Fact]
    public void An_unanswered_request_lapses_and_can_no_longer_be_allowed()
    {
        var (gate, settings) = (new PhoneGate(), new PhoneSettings());
        gate.Check(settings, WhoIs(Pixel, "pixel"), "1", 0);

        Assert.Null(gate.Answer(Pixel, true, PhoneGate.PendingForMs));
        Assert.Empty(gate.Pending);
    }

    [Fact]
    public void A_removed_device_has_to_ask_again()
    {
        var gate = new PhoneGate();
        var settings = new PhoneSettings().Allow(Pixel, "pixel", 0);

        settings = settings.Forget(Pixel);

        Assert.False(settings.Allows(Pixel));
        Assert.Equal(Waiting(true, "1"), gate.Check(settings, WhoIs(Pixel, "pixel"), "1", 1));
    }

    [Fact]
    public void Answering_a_device_that_is_not_waiting_does_nothing() =>
        Assert.Null(new PhoneGate().Answer(Pixel, true, 0));
}
