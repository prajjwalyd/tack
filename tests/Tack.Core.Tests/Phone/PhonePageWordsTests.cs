using System.Globalization;
using Tack.Core.Phone;

namespace Tack.Core.Tests.Phone;

public class PhonePageWordsTests
{
    const long Now = 1_700_000_000_000;

    static readonly PhoneState s_off = new();

    static Asking AskingOf(string key, string name) => new(key, name, $"{name}.netbird.cloud", "100.90.1.2", "4821", 0);

    [Fact]
    public void The_body_follows_what_is_missing_NetBird_the_switch_the_connection_then_the_code()
    {
        var missing = PhoneWords.Body(s_off with { NetBird = NetBirdState.Missing });
        Assert.Equal(PhoneBodyKind.GetNetBird, missing.Kind);
        Assert.StartsWith("Tack reaches your phone through NetBird, a free, open-source private network.", missing.Text, StringComparison.Ordinal);
        Assert.EndsWith("sign in to the same account on both.", missing.Text, StringComparison.Ordinal);

        Assert.Equal(new PhoneBody(PhoneBodyKind.Off, "Only devices on your NetBird network can ask, and only ones you allow get in."),
            PhoneWords.Body(s_off));
        Assert.Equal(new PhoneBody(PhoneBodyKind.Disconnected, "NetBird isn't connected on this PC. Connect it, and Tack picks it up."),
            PhoneWords.Body(new PhoneState { On = true, NetBird = NetBirdState.Disconnected }));
        Assert.Equal(new PhoneBody(PhoneBodyKind.GettingReady, "Getting ready…"), PhoneWords.Body(new PhoneState { On = true }));
        Assert.Equal(new PhoneBody(PhoneBodyKind.Problem, "Couldn't start: <x>"),
            PhoneWords.Body(new PhoneState { On = true, Error = "Couldn't start: <x>" }));
    }

    [Fact]
    public void The_address_is_shown_with_the_QR_code_when_there_is_one()
    {
        var serving = new PhoneState { On = true, Serving = true, Address = "http://my-pc.netbird.cloud:7717" };
        var qr = new QrMatrix(1, [true]);

        var withCode = PhoneWords.Body(serving with { Qr = qr });
        Assert.Equal((PhoneBodyKind.Share, "Scan with your phone's camera"), (withCode.Kind, withCode.Text));
        Assert.Equal(("http://my-pc.netbird.cloud:7717", qr), (withCode.Address, withCode.Qr));
        Assert.Equal("Open this on your phone", PhoneWords.Body(serving).Text);
        // Serving with no address yet is still getting ready.
        Assert.Equal(PhoneBodyKind.GettingReady, PhoneWords.Body(serving with { Address = null }).Kind);
    }

    [Fact]
    public void A_request_names_the_device_its_address_and_the_code_to_compare()
    {
        var pixel = AskingOf("k", "pixel");

        Assert.Equal("pixel wants to use your board", PhoneWords.AskTitle(pixel));
        Assert.Equal("pixel.netbird.cloud · 100.90.1.2", PhoneWords.AskWho(pixel));
        Assert.Equal("Allow only if your phone shows ", PhoneWords.AskCodeLead);
        Assert.Equal("pixel can use your board", PhoneWords.Answered("pixel", true));
        Assert.Equal("The device was not allowed", PhoneWords.Answered("", false));
        Assert.Equal("pixel is asking to use your board", PhoneWords.NewAsks([pixel]));
        Assert.Equal("pixel and ipad are asking to use your board", PhoneWords.NewAsks([pixel, AskingOf("j", "ipad")]));
    }

    [Fact]
    public void An_allowed_device_says_when_it_was_allowed()
    {
        static string Allowed(long ago) => PhoneWords.Allowed(
            new Device { Key = "k", Name = "pixel", ApprovedAt = (ulong)(Now - ago) }, Now, CultureInfo.InvariantCulture, TimeZoneInfo.Utc);

        Assert.Equal("Allowed just now", Allowed(0));
        Assert.Equal("Allowed 3 hours ago", Allowed(3 * 3_600_000));
        Assert.StartsWith("Allowed on ", Allowed(3 * 86_400_000), StringComparison.Ordinal);
        Assert.Equal("pixel removed", PhoneWords.Removed("pixel"));
    }
}
