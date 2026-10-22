namespace Tack.Core.Phone;

/// <summary>
/// The phone board as Settings sees it: its state, word when something other
/// than the saved settings changes it (NetBird, serving, a device asking), and
/// the answer to a device asking. Settings renders the state after every
/// change of settings and after every <see cref="Changed"/>.
/// </summary>
public interface IPhoneLink
{
    /// <summary>The state now, with the switch and devices of <paramref name="saved"/>.</summary>
    PhoneState State(PhoneSettings saved);

    /// <summary>The state changed for a reason of its own. Raised on the UI thread.</summary>
    event Action? Changed;

    /// <summary>The person's answer about the device asking with <paramref name="key"/>. False if it could not be given (it is no longer asking, say).</summary>
    bool Answer(string key, bool allow);
}
