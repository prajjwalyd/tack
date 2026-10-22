using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Tack.Core.Pinboard.Words;

namespace Tack.App.Pinboard.Automation;

/// <summary>
/// The board for screen readers: a horizontal list named "Tack board" whose
/// items are the row's prints, in order, and nothing else (the felt, the hint
/// card and the captions are only pictures). One print at most is selected,
/// the one with the keyboard's focus. An empty board says how to fill it.
/// </summary>
sealed partial class BoardListPeer(FrameworkElement owner, BoardAutomation automation)
    : FrameworkElementAutomationPeer(owner), ISelectionProvider
{
    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => false;

    public IRawElementProviderSimple[] GetSelection() =>
        automation.Selected is { } item && automation.PeerOf(item) is { } peer ? [ProviderFromPeer(peer)] : [];

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => "TackBoard";

    protected override string GetNameCore() => Labels.Board;

    protected override string GetFullDescriptionCore() => automation.IsEmpty ? Labels.EmptyBoard : string.Empty;

    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.Horizontal;

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;

    protected override IList<AutomationPeer>? GetChildrenCore() =>
        PeerList.Fill(base.GetChildrenCore(), automation.Row.Select(automation.PeerOf).OfType<AutomationPeer>());

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Selection ? this : base.GetPatternCore(patternInterface);
}
