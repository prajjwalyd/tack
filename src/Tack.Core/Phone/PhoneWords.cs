using System.Globalization;
using Tack.Core.Pinboard.Words;

namespace Tack.Core.Phone;

/// <summary>
/// The words of the phone card and the phone page for a <see cref="PhoneState"/>:
/// the card's status line, the page's body, a request and an allowed device,
/// and what a screen reader is told. Pure, so the wording is easy to check.
/// </summary>
public static class PhoneWords
{
    /// <summary>What the card says until the state is known.</summary>
    public const string Tagline = "See your board on your phone, over NetBird.";

    public const string SetUp = "Set up";
    public const string Manage = "Manage";
    public const string GettingReady = "Getting ready…";

    /// <summary>Where Get NetBird goes: NetBird's own install page, for every platform.</summary>
    public const string NetBirdInstall = "https://docs.netbird.io/get-started/install";

    /// <summary>Said when the board starts being served while the phone page shows.</summary>
    public const string Ready = "Ready. Scan the code with your phone.";

    public const string AddressCopied = "Address copied";
    public const string CopyFailed = "Couldn't copy. The address is selected: press Ctrl+C.";
    public const string AnswerFailed = "Couldn't answer. Try again.";

    /// <summary>The words before a request's code, which follows in bold.</summary>
    public const string AskCodeLead = "Allow only if your phone shows ";

    /// <summary>What the card shows for <paramref name="state"/>; null while it is not known yet.</summary>
    public static PhoneCardView Card(PhoneState? state)
    {
        if (state is null)
        {
            return new(Tagline, PhoneTone.None, SetUp);
        }
        var button = state.On ? Manage : SetUp;
        var pending = state.Pending.Count;
        if (state.NetBird == NetBirdState.Missing)
        {
            return new("NetBird isn't installed", PhoneTone.Warn, button);
        }
        if (!state.On)
        {
            return new("Off", PhoneTone.None, button);
        }
        if (state.NetBird == NetBirdState.Disconnected)
        {
            return new("NetBird isn't connected", PhoneTone.Warn, button);
        }
        if (pending > 0)
        {
            var who = pending == 1 && state.Pending[0].Name.Length > 0 ? state.Pending[0].Name : $"{pending} devices";
            return new($"{who} {(pending == 1 ? "wants" : "want")} to use your board", PhoneTone.Attention, button);
        }
        if (state.Serving)
        {
            return new($"On · {DeviceCount(state.Devices.Count)}", PhoneTone.Ok, button);
        }
        if (!string.IsNullOrEmpty(state.Error))
        {
            return new(state.Error, PhoneTone.Warn, button);
        }
        return new(GettingReady, PhoneTone.None, button);
    }

    /// <summary>What the page shows under its switch for <paramref name="state"/>.</summary>
    public static PhoneBody Body(PhoneState state)
    {
        if (state.NetBird == NetBirdState.Missing)
        {
            return new(PhoneBodyKind.GetNetBird,
                "Tack reaches your phone through NetBird, a free, open-source private network. " +
                "Install it on this PC and on your phone, and sign in to the same account on both.");
        }
        if (!state.On)
        {
            return new(PhoneBodyKind.Off, "Only devices on your NetBird network can ask, and only ones you allow get in.");
        }
        if (state.NetBird == NetBirdState.Disconnected)
        {
            return new(PhoneBodyKind.Disconnected, "NetBird isn't connected on this PC. Connect it, and Tack picks it up.");
        }
        if (state.Serving && !string.IsNullOrEmpty(state.Address))
        {
            var scan = state.Qr is null ? "Open this on your phone" : "Scan with your phone's camera";
            return new(PhoneBodyKind.Share, scan, state.Address, state.Qr);
        }
        if (!string.IsNullOrEmpty(state.Error))
        {
            return new(PhoneBodyKind.Problem, state.Error);
        }
        return new(PhoneBodyKind.GettingReady, GettingReady);
    }

    /// <summary>"On · no devices yet", "On · 1 device", "On · 2 devices": the count after "On · ".</summary>
    public static string DeviceCount(int count) => count switch
    {
        0 => "no devices yet",
        1 => "1 device",
        _ => $"{count} devices",
    };

    /// <summary>A device's name for the page, or "The device" when NetBird gave none.</summary>
    public static string NameOf(string name) => name.Length > 0 ? name : "The device";

    /// <summary>A request's heading: "pixel wants to use your board".</summary>
    public static string AskTitle(Asking asking) => $"{asking.Name} wants to use your board";

    /// <summary>Under a request's heading: "pixel.netbird.cloud · 100.90.1.2".</summary>
    public static string AskWho(Asking asking) => $"{asking.Fqdn} · {asking.Ip}";

    /// <summary>Said after the person answers a request.</summary>
    public static string Answered(string name, bool allowed) =>
        allowed ? $"{NameOf(name)} can use your board" : $"{NameOf(name)} was not allowed";

    /// <summary>Said when devices start asking while the phone page shows: "pixel is asking…", "pixel and ipad are asking…".</summary>
    public static string NewAsks(IReadOnlyList<Asking> fresh) =>
        $"{string.Join(" and ", fresh.Select(a => a.Name))} {(fresh.Count > 1 ? "are" : "is")} asking to use your board";

    /// <summary>Under an allowed device's name: "Allowed 3 hours ago", "Allowed on Tuesday".</summary>
    public static string Allowed(Device device, long nowMs, CultureInfo? culture = null, TimeZoneInfo? zone = null) =>
        "Allowed " + Age.Ago((long)device.ApprovedAt, nowMs, on: "on ", culture: culture, zone: zone);

    /// <summary>Said when a device is removed from the allowed ones.</summary>
    public static string Removed(string name) => $"{NameOf(name)} removed";

    /// <summary>Said when the card's line changes while the list shows.</summary>
    public static string CardAnnouncement(PhoneCardView card) => $"Use on your phone: {card.Status}";
}
