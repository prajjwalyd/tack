using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Words;

namespace Tack.App.Pinboard.Automation;

/// <summary>
/// One print for screen readers: an item of the board's list, named in words
/// ("Screenshot, 2 minutes ago, kept") worked out each time it is asked, so
/// its age is never stale. It is selected while it has the keyboard's focus,
/// and selecting or focusing it moves the keyboard's focus there. Its Keep
/// and unpin buttons are its children.
/// </summary>
sealed partial class PrintItemPeer(FrameworkElement owner, BoardItem item, BoardAutomation automation)
    : FrameworkElementAutomationPeer(owner), ISelectionItemProvider
{
    PrintButtonPeer[]? _buttons;

    public bool IsSelected => automation.Selected == item;

    public IRawElementProviderSimple? SelectionContainer =>
        automation.ListPeer is { } list ? ProviderFromPeer(list) : null;

    public void Select() => automation.Select(item);

    public void AddToSelection() => automation.Select(item);

    /// <summary>The focus always rests on a print: it can move, never be taken away.</summary>
    public void RemoveFromSelection()
    {
    }

    /// <summary>Its name and its buttons' names may have changed (kept, or its note's words).</summary>
    public void Refresh()
    {
        InvalidatePeer();
        foreach (var button in _buttons ?? [])
        {
            button.InvalidatePeer();
        }
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => "TackPrint";

    protected override string GetNameCore() => Labels.For(item.Print, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;

    /// <summary>Every print can take the focus, though only the focused one is a tab stop.</summary>
    protected override bool IsKeyboardFocusableCore() => !item.Leaving;

    protected override void SetFocusCore() => automation.Select(item);

    protected override int GetPositionInSetCore() => automation.IndexOf(item) + 1;

    protected override int GetSizeOfSetCore() => automation.Count;

    /// <summary>Its Keep and unpin buttons; the picture or note's words are its name, not children.</summary>
    protected override IList<AutomationPeer>? GetChildrenCore() => PeerList.Fill(base.GetChildrenCore(), _buttons ??=
    [
        new PrintButtonPeer(item.View.KeepButton.Element, () => Labels.KeepButton(item.Print.Kept), () => automation.Keep(item)),
        new PrintButtonPeer(item.View.XButton.Element, () => Labels.UnpinButton, () => automation.Unpin(item)),
    ]);

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.SelectionItem ? this : base.GetPatternCore(patternInterface);
}
