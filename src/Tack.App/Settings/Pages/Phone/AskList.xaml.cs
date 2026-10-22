using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Phone;

namespace Tack.App.Settings.Pages.Phone;

/// <summary>
/// The requests at the top of the phone page, one <see cref="AskCard"/> per
/// device asking. A security prompt, so: Allow wakes only a moment after the
/// question changes, nothing here ever takes the focus from elsewhere (a click
/// or key meant for something else must not answer it), and the focus moves
/// within it only if it was already there.
/// </summary>
public sealed partial class AskList : UserControl
{
    /// <summary>How long a question is on screen before Allow answers to a click.</summary>
    static readonly TimeSpan s_armAfter = TimeSpan.FromMilliseconds(1000);

    SettingsContext? _context;
    IPhoneLink _link = SavedPhoneLink.Instance;
    DispatcherQueueTimer? _arm;
    IReadOnlyList<Asking>? _shown;

    public AskList() => InitializeComponent();

    /// <summary>Raised when devices start asking, so the page can scroll to the top where they are.</summary>
    public event Action? Arrived;

    /// <summary>Once, with the window's context.</summary>
    internal void Start(SettingsContext context)
    {
        _context = context;
        _arm = context.Ui.CreateTimer();
        _arm.Interval = s_armAfter;
        _arm.IsRepeating = false;
        _arm.Tick += (_, _) => ArmAll();
    }

    /// <summary>Answers come from <paramref name="link"/> from now on.</summary>
    public void Use(IPhoneLink link) => _link = link;

    /// <summary>Shows the devices asking, rebuilding the cards only when the question changed.</summary>
    public void Render(IReadOnlyList<Asking> pending)
    {
        if (_shown is not null && _shown.SequenceEqual(pending))
        {
            return;
        }
        var known = _shown;
        _shown = [.. pending];
        var hadFocus = FocusWithin.Has(this);

        Cards.Children.Clear();
        foreach (var asking in pending)
        {
            Cards.Children.Add(new AskCard(asking, Answer));
        }
        Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Every change of the question starts the wait again.
        _arm?.Stop();
        _arm?.Start();

        if (pending.Count == 0)
        {
            if (hadFocus)
            {
                // The focused button just went away.
                _context?.FocusTop();
            }
            return;
        }
        Arrived?.Invoke();
        var fresh = known is null ? [] : pending.Where(a => !known.Any(k => k.Key == a.Key)).ToList();
        if (fresh.Count > 0 && _context is { OnPhonePage: true })
        {
            _context.Announce(PhoneWords.NewAsks(fresh));
        }
        if (hadFocus && Cards.Children[0] is AskCard first)
        {
            first.Focus(FocusState.Programmatic);
        }
    }

    void ArmAll()
    {
        foreach (var card in Cards.Children.OfType<AskCard>())
        {
            card.Arm();
        }
    }

    void Answer(AskCard card, bool allow)
    {
        card.Answerable(false);
        if (_link.Answer(card.Asking.Key, allow))
        {
            _context?.Announce(PhoneWords.Answered(card.Asking.Name, allow));
        }
        else
        {
            card.Answerable(true);
            _context?.Announce(PhoneWords.AnswerFailed);
        }
    }
}
