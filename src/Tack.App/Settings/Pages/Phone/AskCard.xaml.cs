using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Phone;

namespace Tack.App.Settings.Pages.Phone;

/// <summary>
/// One device asking to use the board: "pixel wants to use your board", its
/// NetBird name and address, and the code its page shows. Allow starts
/// disabled and is woken by <see cref="Arm"/>; Don't allow is always there.
/// The card itself can take the focus, so when a focused button goes away the
/// keyboard can land on the question rather than on an answer.
/// </summary>
public sealed partial class AskCard : UserControl
{
    readonly Action<AskCard, bool> _answer;

    /// <param name="answer">Called with this card and true for Allow, false for Don't allow.</param>
    public AskCard(Asking asking, Action<AskCard, bool> answer)
    {
        InitializeComponent();
        (Asking, _answer) = (asking, answer);
        Title.Text = PhoneWords.AskTitle(asking);
        Who.Text = PhoneWords.AskWho(asking);
        CodeLead.Text = PhoneWords.AskCodeLead;
        Code.Text = asking.Code;
        AutomationProperties.SetName(this, Title.Text);
        AutomationProperties.SetHelpText(this, $"{Who.Text}. {PhoneWords.AskCodeLead}{asking.Code}");
        AutomationProperties.SetName(DontAllow, $"Don't allow {asking.Name}");
        AutomationProperties.SetName(Allow, $"Allow {asking.Name}");
        DontAllow.Click += (_, _) => _answer(this, false);
        Allow.Click += (_, _) => _answer(this, true);
    }

    /// <summary>The request this card asks about.</summary>
    internal Asking Asking { get; }

    /// <summary>Lets Allow answer: the question has been on screen long enough to be read.</summary>
    public void Arm() => Allow.IsEnabled = true;

    /// <summary>While an answer is being given (false), neither button answers again.</summary>
    public void Answerable(bool on)
    {
        DontAllow.IsEnabled = on;
        Allow.IsEnabled = on;
    }
}
