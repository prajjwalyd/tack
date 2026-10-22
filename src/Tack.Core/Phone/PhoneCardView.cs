namespace Tack.Core.Phone;

/// <summary>What the phone card at the top of Settings shows.</summary>
/// <param name="Status">The line under its title.</param>
/// <param name="Tone">The dot before that line.</param>
/// <param name="Button">"Manage" when on, "Set up" when off.</param>
public sealed record PhoneCardView(string Status, PhoneTone Tone, string Button)
{
    /// <summary>"Set up" is the step the person is invited to take, so it is drawn in the accent colour.</summary>
    public bool Accent => Button == PhoneWords.SetUp;
}
